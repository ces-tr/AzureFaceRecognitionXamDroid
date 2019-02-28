using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

//using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Android.Content;
using Android.Gms.Common.Images;
using Android.Gms.Vision;
using Android.Graphics;
using Android.Hardware;
using Android.OS;
using Android.Runtime;
//using Android.Runtime;
using Android.Util;
using Android.Views;

using Java.Nio;
using HWCamera = Android.Hardware.Camera;
using Size = Android.Gms.Common.Images.Size;

namespace FaceRecognitionSimple.CustomControls {

    public enum FocusMode {

        FOCUS_MODE_CONTINUOUS_PICTURE,
        FOCUS_MODE_CONTINUOUS_VIDEO,
        FOCUS_MODE_AUTO,
        FOCUS_MODE_EDOF,
        FOCUS_MODE_FIXED,
        FOCUS_MODE_INFINITY,
        FOCUS_MODE_MACRO
    }

    public enum FlashMode {

        FLASH_MODE_ON,
        FLASH_MODE_OFF,
        FLASH_MODE_AUTO,
        FLASH_MODE_RED_EYE,
        FLASH_MODE_TORCH
    }

    public class FRCameraSource {


        public static int CAMERA_FACING_BACK = (int)HWCamera.CameraInfo.CameraFacingBack;

        public static int CAMERA_FACING_FRONT = (int)HWCamera.CameraInfo.CameraFacingFront;

        private static String TAG = "OpenCameraSource";

        /**
         * The dummy surface texture must be assigned a chosen name.  Since we never use an OpenGL
         * context, we can choose any ID we want here.
         */
        private static int DUMMY_TEXTURE_NAME = 100;

        /**
         * If the absolute difference between a preview size aspect ratio and a picture size aspect
         * ratio is less than this tolerance, they are considered to be the same aspect ratio.
         */
        private static float ASPECT_RATIO_TOLERANCE = 0.01f;


        private Context mContext;

        private static object  mCameraLock = new Object();

        // Guarded by mCameraLock
        private static HWCamera mCamera;

        private int mFacing = (int)CAMERA_FACING_BACK;

        /**
         * Rotation of the device, and thus the associated preview images captured from the device.
         * See {@link Frame.Metadata#getRotation()}.
         */
        private static int mRotation;

        private static Size mPreviewSize;

        // These values may be requested by the caller.  Due to hardware limitations, we may need to
        // select close, but not exactly the same values for these.
        private float mRequestedFps = 30.0f;
        private int mRequestedPreviewWidth = 1024;
        private int mRequestedPreviewHeight = 768;


        private String mFocusMode = null;
        private String mFlashMode = null;

        // These instances need to be held onto to avoid GC of their underlying resources.  Even though
        // these aren't used outside of the method that creates them, they still must have hard
        // references maintained to them.
        private SurfaceView mDummySurfaceView;
        private SurfaceTexture mDummySurfaceTexture;

        /**
         * Dedicated thread and associated runnable for calling into the detector with frames, as the
         * frames become available from the camera.
         */
        private static Java.Lang.Thread mProcessingThread;

        private static FrameProcessingRunnable mFrameProcessor;

        private static ConcurrentDictionary<byte[], ByteBuffer> mBytesToByteBuffer = new ConcurrentDictionary<byte[], ByteBuffer>();

        public static Dictionary<FocusMode, string> HWCameraParametersFocusMode = new Dictionary<FocusMode, string>(){
            {FocusMode.FOCUS_MODE_CONTINUOUS_PICTURE , HWCamera.Parameters.FocusModeContinuousPicture},
            {FocusMode.FOCUS_MODE_CONTINUOUS_VIDEO, HWCamera.Parameters.FocusModeContinuousVideo},
            {FocusMode.FOCUS_MODE_AUTO, HWCamera.Parameters.FocusModeAuto },
            {FocusMode.FOCUS_MODE_EDOF ,HWCamera.Parameters.FocusModeEdof},
            {FocusMode.FOCUS_MODE_FIXED, HWCamera.Parameters.FocusModeFixed},
            {FocusMode.FOCUS_MODE_INFINITY, HWCamera.Parameters.FocusModeInfinity},
            {FocusMode.FOCUS_MODE_MACRO, HWCamera.Parameters.FocusModeMacro}
        };

        public static Dictionary<FlashMode, string> HWCameraParametersFlashMode = new Dictionary<FlashMode, string>()
        {
            {FlashMode.FLASH_MODE_ON , HWCamera.Parameters.FlashModeOn},
            {FlashMode.FLASH_MODE_OFF,  HWCamera.Parameters.FlashModeOff},
            {FlashMode.FLASH_MODE_AUTO,  HWCamera.Parameters.FlashModeAuto},
            {FlashMode.FLASH_MODE_RED_EYE,  HWCamera.Parameters.FlashModeRedEye},
            {FlashMode.FLASH_MODE_TORCH,  HWCamera.Parameters.FlashModeTorch}

        };

        public class Builder {

            private readonly Detector mDetector;
            private FRCameraSource mCameraSource = new FRCameraSource();

            /**
             * Creates a camera source builder with the supplied context and detector.  Camera preview
             * images will be streamed to the associated detector upon starting the camera source.
             */
            public Builder(Context context, Detector detector)
            {
                if (context == null) {
                    throw new Exception("No context supplied.");
                }
                if (detector == null) {
                    throw new Exception("No detector supplied.");
                }

                mDetector = detector;
                mCameraSource.mContext = context;
            }

            /**
             * Sets the requested frame rate in frames per second.  If the exact requested value is not
             * not available, the best matching available value is selected.   Default: 30.
             */
            public Builder setRequestedFps(float fps)
            {
                if (fps <= 0) {
                    throw new Exception("Invalid fps: " + fps);
                }
                mCameraSource.mRequestedFps = fps;
                return this;
            }

            public Builder setFocusMode(FocusMode mode)
            {
                string focusMode = HWCameraParametersFocusMode.FirstOrDefault(m => m.Equals(mode)).Value;
                mCameraSource.mFocusMode = focusMode;
                return this;
            }

            public Builder setFlashMode(FlashMode mode)
            {
                string flashMode = HWCameraParametersFlashMode.FirstOrDefault(m => m.Equals(mode)).Value;
                mCameraSource.mFlashMode = flashMode;
                return this;
            }

            /**
             * Sets the desired width and height of the camera frames in pixels.  If the exact desired
             * values are not available options, the best matching available options are selected.
             * Also, we try to select a preview size which corresponds to the aspect ratio of an
             * associated full picture size, if applicable.  Default: 1024x768.
             */
            public Builder SetRequestedPreviewSize(int width, int height)
            {
                // Restrict the requested range to something within the realm of possibility.  The
                // choice of 1000000 is a bit arbitrary -- intended to be well beyond resolutions that
                // devices can support.  We bound this to avoid int overflow in the code later.
                int MAX = 1000000;
                if ((width <= 0) || (width > MAX) || (height <= 0) || (height > MAX)) {
                    throw new Exception("Invalid preview size: " + width + "x" + height);
                }
                mCameraSource.mRequestedPreviewWidth = width;
                mCameraSource.mRequestedPreviewHeight = height;
                return this;
            }

            /**
             * Sets the camera to use (either {@link #CAMERA_FACING_BACK} or
             * {@link #CAMERA_FACING_FRONT}). Default: back facing.
             */
            public Builder SetFacing(Android.Gms.Vision.CameraFacing facing)
            {
                int intfacing = (int)facing;
                if ((intfacing != CAMERA_FACING_BACK) && (intfacing != CAMERA_FACING_FRONT)) {
                    throw new Exception("Invalid camera: " + facing);
                }
                mCameraSource.mFacing = intfacing;
                return this;
            }

            public Builder SetAutoFocusEnabled(bool enableautofocus)
            {

                return this;
            }

            public Builder SetRequestedFps(float fps)
            {
                return this;
            }

            /**
             * Creates an instance of the camera source.
             */
            public FRCameraSource Build()
            {
                mFrameProcessor = new FrameProcessingRunnable(mDetector);

                return mCameraSource;
            }
        }

        class FrameProcessingRunnable : Java.Lang.Object, Java.Lang.IRunnable {

            //private static String TAG = "OpenCameraSource";

            private Detector mDetector;
            private long mStartTimeMillis = SystemClock.ElapsedRealtime();

            // This lock guards all of the member variables below.
            private Object mLockobject = new Object();
            TaskCompletionSource<bool> mlock = new TaskCompletionSource<bool>();

            private bool mActive = true;

            // These pending variables hold the state associated with the new frame awaiting processing.
            private long mPendingTimeMillis;
            private int mPendingFrameId = 0;
            private ByteBuffer mPendingFrameData;



            public FrameProcessingRunnable(Detector detector)
            {
                mDetector = detector;
            }

            /**
             * Releases the underlying receiver.  This is only safe to do after the associated thread
             * has completed, which is managed in camera source's release method above.
             */

            public void Release()
            {
                //assert(mProcessingThread.getState() == State.TERMINATED);
                if (mProcessingThread?.GetState() != Java.Lang.Thread.State.Terminated) {
                    //throw new Exception($"Error releasing thread: {mProcessingThread?.GetState()}");
                }

                mDetector.Release();
                mDetector = null;
            }

            /**
             * Marks the runnable as active/not active.  Signals any blocked threads to continue.
             */
            [MethodImpl(MethodImplOptions.Synchronized)]
            public void setActive(bool active)
            {
                lock (mLockobject) {
                    mActive = active;
                    mlock.TrySetResult(true);
                    //mLock.NotifyAll();
                }
            }

            /**
             * Sets the frame data received from the camera.  This adds the previous unused frame buffer
             * (if present) back to the camera, and keeps a pending reference to the frame data for
             * future use.
             */
            public void setNextFrame(byte[] data, HWCamera camera)
            {
                lock (mLockobject) {
                    if (mPendingFrameData != null) {
                        camera.AddCallbackBuffer(mPendingFrameData.ToArray<byte>());
                        mPendingFrameData = null;
                    }

                    if (data==null || !mBytesToByteBuffer.ContainsKey(data)) {
                        Log.Debug(TAG,
                            "Skipping frame.  Could not find ByteBuffer associated with the image " +
                            "data from the camera.");
                        return;
                    }

                    // Timestamp and frame ID are maintained here, which will give downstream code some
                    // idea of the timing of frames received and when frames were dropped along the way.
                    mPendingTimeMillis = SystemClock.ElapsedRealtime() - mStartTimeMillis;
                    mPendingFrameId++;
                    mPendingFrameData = mBytesToByteBuffer.GetValueOrDefault(data);

                    // Notify the processor thread if it is waiting on the next frame (see below).
                    //mLock.NotifyAll();
                    //mlock.TrySetResult(true);
                }
            }

            /**
             * As long as the processing thread is active, this executes detection on frames
             * continuously.  The next pending frame is either immediately available or hasn't been
             * received yet.  Once it is available, we transfer the frame info to local variables and
             * run detection on that frame.  It immediately loops back for the next frame without
             * pausing.
             * <p/>
             * If detection takes longer than the time in between new frames from the camera, this will
             * mean that this loop will run without ever waiting on a frame, avoiding any context
             * switching or frame acquisition time latency.
             * <p/>
             * If you find that this is using more CPU than you'd like, you should probably decrease the
             * FPS setting above to allow for some idle time in between frames.
             */
            byte[] temp = new byte[0];
            public void Run()
            {
                Frame outputFrame = null;
                Java.Nio.ByteBuffer data;

                while (true) {
                    lock (mLockobject) {
                        //while (mActive && (mPendingFrameData == null)) {
                        //    try {
                        //        // Wait for the next frame to be received from the camera, since we
                        //        // don't have it yet.
                        //         //mlock.Task.Wait();
                        //    }
                        //    catch (Exception e) {
                        //        Log.Debug(TAG, "Frame processing loop terminated.", e);
                        //        return;
                        //    }
                        //}

                        if (!mActive) {
                            // Exit the loop once this camera source is stopped or released.  We check
                            // this here, immediately after the wait() above, to handle the case where
                            // setActive(false) had been called, triggering the termination of this
                            // loop.
                            return;
                        }
                        if(mPendingFrameData != null) { 
                            outputFrame = new Frame.Builder()
                                    .SetImageData(mPendingFrameData, mPreviewSize.Width,
                                            mPreviewSize.Height, (int)ImageFormat.Nv21)
                                    .SetId(mPendingFrameId)
                                    .SetTimestampMillis(mPendingTimeMillis)
                                    .SetRotation((Android.Gms.Vision.FrameRotation)mRotation)
                                    .Build();
                        }
                        // Hold onto the frame data locally, so that we can use this for detection
                        // below.  We need to clear mPendingFrameData to ensure that this buffer isn't
                        // recycled back to the camera before we are done using that data.
                        data = mPendingFrameData;
                        mPendingFrameData = null;
                    }

                    // The code below needs to run outside of synchronization, because this will allow
                    // the camera to add pending frame(s) while we are running detection on the current
                    // frame.

                    try {
                        if(outputFrame!= null)
                            mDetector.ReceiveFrame(outputFrame);
                    }
                    catch (Exception t) {
                        Log.Error(TAG, "Exception thrown from receiver.", t);
                    }
                    finally {
                        if (data == null) {

                        }
                        //mCamera.AddCallbackBuffer(temp);
                        else
                            mCamera.AddCallbackBuffer(data.ToArray<byte>());
                    }
                }
            }

        }

        //==============================================================================================
        // Public
        //==============================================================================================

        /**
         * Stops the camera and releases the resources of the camera and underlying detector.
         */
        public void Release()
        {
            lock (mCameraLock) {
                Stop();
                mFrameProcessor.Release();
            }
        }

        /**
         * Opens the camera and starts sending preview frames to the underlying detector.  The preview
         * frames are not displayed.
         *
         * @throws IOException if the camera's preview texture or display could not be initialized
         */
        //@RequiresPermission(Manifest.permission.CAMERA)
        public FRCameraSource Start() {

            lock (mCameraLock) {
                if (mCamera != null) {
                    return this;
                }

                mCamera = createCamera();

                // SurfaceTexture was introduced in Honeycomb (11), so if we are running and
                // old version of Android. fall back to use SurfaceView.
                if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Honeycomb) {
                    mDummySurfaceTexture = new SurfaceTexture(DUMMY_TEXTURE_NAME);
                    mCamera.SetPreviewTexture(mDummySurfaceTexture);
                }
                else {
                    mDummySurfaceView = new SurfaceView(mContext);
                    mCamera.SetPreviewDisplay(mDummySurfaceView.Holder);
                }
                mCamera.StartPreview();

                mProcessingThread = new Java.Lang.Thread(mFrameProcessor);
                mFrameProcessor.setActive(true);
                mProcessingThread.Start();
            }
            return this;
        }

        /**
     * Opens the camera and starts sending preview frames to the underlying detector.  The supplied
     * surface holder is used for the preview so frames can be displayed to the user.
     *
     * @param surfaceHolder the surface holder to use for the preview frames
     * @throws IOException if the supplied surface holder could not be used as the preview display
     */
        //@RequiresPermission(Manifest.permission.CAMERA)
        public FRCameraSource Start(ISurfaceHolder surfaceHolder) {
            lock (mCameraLock) {
                if (mCamera != null) {
                    return this;
                }

                mCamera = createCamera();
                mCamera.SetPreviewDisplay(surfaceHolder);
                mCamera.StartPreview();

                mProcessingThread = new Java.Lang.Thread(mFrameProcessor);
                mFrameProcessor.setActive(true);
                mProcessingThread.Start();
            }
            return this;
        }

        /*
         * Closes the camera and stops sending frames to the underlying frame detector.
         * <p/>
         * This camera source may be restarted again by calling {@link #start()} or
         * {@link #start(SurfaceHolder)}.
         * <p/>
         * Call {@link #release()} instead to completely shut down this camera source and release the
         * resources of the underlying detector.
         */
        public void Stop()
        {
            lock (mCameraLock) {
                mFrameProcessor.setActive(false);
                if (mProcessingThread != null) {
                    try {
                        // Wait for the thread to complete to ensure that we can't have multiple threads
                        // executing at the same time (i.e., which would happen if we called start too
                        // quickly after stop).
                        mProcessingThread.Join();
                    }
                    catch (Java.Lang.InterruptedException e) {
                        Log.Debug(TAG, "Frame processing thread interrupted on release.");
                    }
                    mProcessingThread = null;
                }

                // clear the buffer to prevent oom exceptions
                mBytesToByteBuffer.Clear();

                if (mCamera != null) {
                    mCamera.StopPreview();
                    mCamera.SetPreviewCallbackWithBuffer(null);
                    try {
                        // We want to be compatible back to Gingerbread, but SurfaceTexture
                        // wasn't introduced until Honeycomb.  Since the interface cannot use a SurfaceTexture, if the
                        // developer wants to display a preview we must use a SurfaceHolder.  If the developer doesn't
                        // want to display a preview we use a SurfaceTexture if we are running at least Honeycomb.

                        if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Honeycomb) {
                            mCamera.SetPreviewTexture(null);

                        }
                        else {
                            mCamera.SetPreviewDisplay(null);
                        }
                    }
                    catch (Exception e) {
                        Log.Error(TAG, "Failed to clear camera preview: " + e);
                    }
                    mCamera.Release();
                    mCamera = null;
                }
            }
        }

        /*
         * Returns the preview size that is currently in use by the underlying camera.
         */
        public Size PreviewSize {
            get => mPreviewSize;
        }
       

        /**
     * Returns the selected camera; one of {@link #CAMERA_FACING_BACK} or
     * {@link #CAMERA_FACING_FRONT}.
     */
        public Android.Gms.Vision.CameraFacing CameraFacing { get => (Android.Gms.Vision.CameraFacing)mFacing; }


        public int doZoom(float scale)
        {
            lock (mCameraLock) {
                if (mCamera == null) {
                    return 0;
                }
                int currentZoom = 0;
                int maxZoom;
                HWCamera.Parameters parameters = mCamera.GetParameters();
                if (!parameters.IsZoomSupported) {
                    Log.Warn(TAG, "Zoom is not supported on this device");
                    return currentZoom;
                }
                maxZoom = parameters.MaxZoom;

                currentZoom = parameters.Zoom + 1;
                float newZoom;
                if (scale > 1) {
                    newZoom = currentZoom + scale * (maxZoom / 10);
                }
                else {
                    newZoom = currentZoom * scale;
                }
                currentZoom = (int)(Math.Round(newZoom) - 1);
                if (currentZoom < 0) {
                    currentZoom = 0;
                }
                else if (currentZoom > maxZoom) {
                    currentZoom = maxZoom;
                }
                parameters.Zoom = currentZoom;
                mCamera.SetParameters(parameters);
                return currentZoom;
            }
        }

        /**
         * Initiates taking a picture, which happens asynchronously.  The camera source should have been
         * activated previously with {@link #start()} or {@link #start(SurfaceHolder)}.  The camera
         * preview is suspended while the picture is being taken, but will resume once picture taking is
         * done.
         *
         * @param shutter the callback for image capture moment, or null
         * @param jpeg    the callback for JPEG image data, or null
         */
        public void TakePicture(IShutterCallback shutter, IPictureCallback jpeg)
        {
            lock (mCameraLock) {
                if (mCamera != null) {
                    PictureStartCallback startCallback = new PictureStartCallback();
                    startCallback.mDelegate = shutter;
                    PictureDoneCallback doneCallback = new PictureDoneCallback();
                    doneCallback.mDelegate = jpeg;
                    mCamera.TakePicture(startCallback, null, null, doneCallback);
                }
            }
        }

        /**
         * Gets the current focus mode setting.
         *
         * @return current focus mode. This value is null if the camera is not yet created. Applications should call {@link
         * #autoFocus(AutoFocusCallback)} to start the focus if focus
         * mode is FOCUS_MODE_AUTO or FOCUS_MODE_MACRO.
         * @see Camera.Parameters#FOCUS_MODE_AUTO
         * @see Camera.Parameters#FOCUS_MODE_INFINITY
         * @see Camera.Parameters#FOCUS_MODE_MACRO
         * @see Camera.Parameters#FOCUS_MODE_FIXED
         * @see Camera.Parameters#FOCUS_MODE_EDOF
         * @see Camera.Parameters#FOCUS_MODE_CONTINUOUS_VIDEO
         * @see Camera.Parameters#FOCUS_MODE_CONTINUOUS_PICTURE
         */
        //@Nullable
        //@FocusMode
        public String getFocusMode()
        {
            return mFocusMode;
        }

        /**
         * Sets the focus mode.
         *
         * @param mode the focus mode
         * @return {@code true} if the focus mode is set, {@code false} otherwise
         * @see #getFocusMode()
         */
        public bool setFocusMode(FocusMode mode)
        {
            lock (mCameraLock) {
                string focusMode = HWCameraParametersFocusMode.FirstOrDefault(m => m.Equals(mode)).Value;

                if (mCamera != null && !string.IsNullOrEmpty(focusMode)) {
                    HWCamera.Parameters parameters = mCamera.GetParameters();

                    if (parameters.SupportedFocusModes.Contains(focusMode)) {
                        parameters.FocusMode = focusMode;
                        mCamera.SetParameters(parameters);
                        mFocusMode = focusMode;
                        return true;
                    }
                }

                return false;
            }
        }

        /**
     * Gets the current flash mode setting.
     *
     * @return current flash mode. null if flash mode setting is not
     * supported or the camera is not yet created.
     * @see Camera.Parameters#FLASH_MODE_OFF
     * @see Camera.Parameters#FLASH_MODE_AUTO
     * @see Camera.Parameters#FLASH_MODE_ON
     * @see Camera.Parameters#FLASH_MODE_RED_EYE
     * @see Camera.Parameters#FLASH_MODE_TORCH
     */
        //@Nullable
        //@FlashMode
        public String getFlashMode()
        {
            return mFlashMode;
        }

        /**
         * Sets the flash mode.
         *
         * @param mode flash mode.
         * @return {@code true} if the flash mode is set, {@code false} otherwise
         * @see #getFlashMode()
         */
        public bool setFlashMode(FlashMode mode)
        {
            lock (mCameraLock) {
                string flashMode = HWCameraParametersFlashMode.FirstOrDefault(m => m.Equals(mode)).Value;
                if (mCamera != null && !string.IsNullOrEmpty(flashMode)) {

                    HWCamera.Parameters parameters = mCamera.GetParameters();
                    if (parameters.SupportedFlashModes.Contains(flashMode)) {
                        parameters.FlashMode = flashMode;
                        mCamera.SetParameters(parameters);
                        mFlashMode = flashMode;
                        return true;
                    }
                }

                return false;
            }
        }

        /**
         * Starts camera auto-focus and registers a callback function to run when
         * the camera is focused.  This method is only valid when preview is active
         * (between {@link #start()} or {@link #start(SurfaceHolder)} and before {@link #stop()} or {@link #release()}).
         * <p/>
         * <p>Callers should check
         * {@link #getFocusMode()} to determine if
         * this method should be called. If the camera does not support auto-focus,
         * it is a no-op and {@link AutoFocusCallback#onAutoFocus(boolean)}
         * callback will be called immediately.
         * <p/>
         * <p>If the current flash mode is not
         * {@link Camera.Parameters#FLASH_MODE_OFF}, flash may be
         * fired during auto-focus, depending on the driver and camera hardware.<p>
         *
         * @param cb the callback to run
         * @see #cancelAutoFocus()
         */
        public void autoFocus(AutoFocusCallback cb)
        {
            lock (mCameraLock) {
                if (mCamera != null) {
                    CameraAutoFocusCallback autoFocusCallback = null;
                    if (cb != null) {
                        autoFocusCallback = new CameraAutoFocusCallback();
                        autoFocusCallback.mDelegate = cb;
                    }
                    mCamera.AutoFocus(autoFocusCallback);
                }
            }
        }

        /**
     * Cancels any auto-focus function in progress.
     * Whether or not auto-focus is currently in progress,
     * this function will return the focus position to the default.
     * If the camera does not support auto-focus, this is a no-op.
     *
     * @see #autoFocus(AutoFocusCallback)
     */
        public void cancelAutoFocus()
        {
            lock (mCameraLock) {
                if (mCamera != null) {
                    mCamera.CancelAutoFocus();
                }
            }
        }

        /**
         * Sets camera auto-focus move callback.
         *
         * @param cb the callback to run
         * @return {@code true} if the operation is supported (i.e. from Jelly Bean), {@code false} otherwise
         */
        //@TargetApi(Build.VERSION_CODES.JELLY_BEAN)
        public bool setAutoFocusMoveCallback(AutoFocusMoveCallback cb)
        {
            if (Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.JellyBean) {
                return false;
            }

            lock (mCameraLock) {
                if (mCamera != null) {
                    CameraAutoFocusMoveCallback autoFocusMoveCallback = null;
                    if (cb != null) {
                        autoFocusMoveCallback = new CameraAutoFocusMoveCallback();
                        autoFocusMoveCallback.mDelegate = cb;
                    }
                    mCamera.SetAutoFocusMoveCallback(autoFocusMoveCallback);
                }
            }

            return true;
        }


        private FRCameraSource()
        {
        }

        /**
     * Wraps the camera1 shutter callback so that the deprecated API isn't exposed.
     */
        private class PictureStartCallback : Java.Lang.Object, HWCamera.IShutterCallback {
            public IShutterCallback mDelegate;

            //@Override
            public void OnShutter()
            {
                if (mDelegate != null) {
                    mDelegate.OnShutter();
                }
            }
        }

        /**
         * Wraps the final callback in the camera sequence, so that we can automatically turn the camera
         * preview back on after the picture has been taken.
         */
        public class PictureDoneCallback : Java.Lang.Object, HWCamera.IPictureCallback {
            public IPictureCallback mDelegate;

            //@Override
            public void OnPictureTaken(byte[] data, HWCamera camera)
            {
                if (mDelegate != null) {
                    mDelegate.OnPictureTaken(data);
                }
                lock (mCameraLock) {
                    if (mCamera != null) {
                        mCamera.StartPreview();
                    }
                }
            }
        }

        /**
         * Wraps the camera1 auto focus callback so that the deprecated API isn't exposed.
         */
        private class CameraAutoFocusCallback : Java.Lang.Object, HWCamera.IAutoFocusCallback {
            public AutoFocusCallback mDelegate;

            //@Override
            public void OnAutoFocus(bool success, HWCamera camera)
            {
                if (mDelegate != null) {
                    mDelegate.OnAutoFocus(success);
                }
            }
        }

        /**
         * Wraps the camera1 auto focus move callback so that the deprecated API isn't exposed.
         */
        //@TargetApi(Build.VERSION_CODES.JELLY_BEAN)
        private class CameraAutoFocusMoveCallback : Java.Lang.Object, HWCamera.IAutoFocusMoveCallback {
            public AutoFocusMoveCallback mDelegate;

            //@Override
            public void OnAutoFocusMoving(bool start, HWCamera camera)
            {
                if (mDelegate != null) {
                    mDelegate.OnAutoFocusMoving(start);
                }
            }
        }


        /**
     * Opens the camera and applies the user settings.
     *
     * @throws RuntimeException if the method fails
     */
        //@SuppressLint("InlinedApi")
        private HWCamera createCamera()
        {
            int requestedCameraId = getIdForRequestedCamera(mFacing);
            if (requestedCameraId == -1) {
                throw new Exception("Could not find requested camera.");
            }
            HWCamera camera = HWCamera.Open(requestedCameraId);

            SizePair sizePair = selectSizePair(camera, mRequestedPreviewWidth, mRequestedPreviewHeight);
            if (sizePair == null) {
                throw new Exception("Could not find suitable preview size.");
            }
            Size pictureSize = sizePair.pictureSize();
            mPreviewSize = sizePair.previewSize();

            int[] previewFpsRange = selectPreviewFpsRange(camera, mRequestedFps);
            if (previewFpsRange == null) {
                throw new Exception("Could not find suitable preview frames per second range.");
            }

            HWCamera.Parameters parameters = camera.GetParameters();

            if (pictureSize != null) {
                parameters.SetPictureSize(pictureSize.Width, pictureSize.Height);
            }

            parameters.SetPreviewSize(mPreviewSize.Width, mPreviewSize.Height);
            parameters.SetPreviewFpsRange(
                    previewFpsRange[(int)HWCamera.Parameters.PreviewFpsMinIndex],
                    previewFpsRange[(int)HWCamera.Parameters.PreviewFpsMaxIndex]);
            parameters.PreviewFormat = (ImageFormat.Nv21);

            setRotation(camera, parameters, requestedCameraId);

            if (mFocusMode != null) {
                if (parameters.SupportedFocusModes.Contains(
                        mFocusMode)) {
                    parameters.FocusMode = (mFocusMode);
                }
                else {
                    Log.Info(TAG, "Camera focus mode: " + mFocusMode + " is not supported on this device.");
                }
            }

            // setting mFocusMode to the one set in the params
            mFocusMode = parameters.FocusMode;

            if (mFlashMode != null) {
                if (parameters.SupportedFocusModes != null) {
                    if (parameters.SupportedFocusModes.Contains(
                            mFlashMode)) {
                        parameters.FlashMode = (mFlashMode);
                    }
                    else {
                        Log.Info(TAG, "Camera flash mode: " + mFlashMode + " is not supported on this device.");
                    }
                }
            }

            // setting mFlashMode to the one set in the params
            mFlashMode = parameters.FlashMode;

            camera.SetParameters(parameters);

            // Four frame buffers are needed for working with the camera:
            //
            //   one for the frame that is currently being executed upon in doing detection
            //   one for the next pending frame to process immediately upon completing detection
            //   two for the frames that the camera uses to populate future preview images
            camera.SetPreviewCallbackWithBuffer(new CameraPreviewCallback());
            camera.AddCallbackBuffer(createPreviewBuffer(mPreviewSize));
            camera.AddCallbackBuffer(createPreviewBuffer(mPreviewSize));
            camera.AddCallbackBuffer(createPreviewBuffer(mPreviewSize));
            camera.AddCallbackBuffer(createPreviewBuffer(mPreviewSize));

            return camera;
        }

        /**
     * Gets the id for the camera specified by the direction it is facing.  Returns -1 if no such
     * camera was found.
     *
     * @param facing the desired camera (front-facing or rear-facing)
     */
        private static int getIdForRequestedCamera(int facing)
        {
            HWCamera.CameraInfo cameraInfo = new HWCamera.CameraInfo();
            for (int i = 0; i < HWCamera.NumberOfCameras; ++i) {
                HWCamera.GetCameraInfo(i, cameraInfo);
                if (cameraInfo.Facing == (Android.Hardware.CameraFacing)facing) {
                    return i;
                }
            }
            return -1;
        }

        /**
         * Selects the most suitable preview and picture size, given the desired width and height.
         * <p/>
         * Even though we may only need the preview size, it's necessary to find both the preview
         * size and the picture size of the camera together, because these need to have the same aspect
         * ratio.  On some hardware, if you would only set the preview size, you will get a distorted
         * image.
         *
         * @param camera        the camera to select a preview size from
         * @param desiredWidth  the desired width of the camera preview frames
         * @param desiredHeight the desired height of the camera preview frames
         * @return the selected preview and picture size pair
         */
        private static SizePair selectSizePair(HWCamera camera, int desiredWidth, int desiredHeight)
        {
            List<SizePair> validPreviewSizes = generateValidPreviewSizeList(camera);

            // The method for selecting the best size is to minimize the sum of the differences between
            // the desired values and the actual values for width and height.  This is certainly not the
            // only way to select the best size, but it provides a decent tradeoff between using the
            // closest aspect ratio vs. using the closest pixel area.
            SizePair selectedPair = null;
            int minDiff = int.MaxValue;


            foreach (var sizePair in validPreviewSizes) {
                Size size = sizePair.previewSize();
                int diff = Math.Abs(size.Width - desiredWidth) +
                        Math.Abs(size.Height - desiredHeight);
                if (diff < minDiff) {
                    selectedPair = sizePair;
                    minDiff = diff;
                }
            }

            return selectedPair;
        }

        /**
         * Stores a preview size and a corresponding same-aspect-ratio picture size.  To avoid distorted
         * preview images on some devices, the picture size must be set to a size that is the same
         * aspect ratio as the preview size or the preview may end up being distorted.  If the picture
         * size is null, then there is no picture size with the same aspect ratio as the preview size.
         */
        public class SizePair {
            private Size mPreview;
            private Size mPicture;

            public SizePair(HWCamera.Size previewSize,
                            HWCamera.Size pictureSize)
            {
                mPreview = new Size(previewSize.Width, previewSize.Height);
                if (pictureSize != null) {
                    mPicture = new Size(pictureSize.Width, pictureSize.Height);
                }
            }

            public Size previewSize()
            {
                return mPreview;
            }

            //@SuppressWarnings("unused")
            public Size pictureSize()
            {
                return mPicture;
            }
        }

        /**
         * Generates a list of acceptable preview sizes.  Preview sizes are not acceptable if there is
         * not a corresponding picture size of the same aspect ratio.  If there is a corresponding
         * picture size of the same aspect ratio, the picture size is paired up with the preview size.
         * <p/>
         * This is necessary because even if we don't use still pictures, the still picture size must be
         * set to a size that is the same aspect ratio as the preview size we choose.  Otherwise, the
         * preview images may be distorted on some devices.
         */
        private static List<SizePair> generateValidPreviewSizeList(HWCamera camera)
        {
            HWCamera.Parameters parameters = camera.GetParameters();
            List<HWCamera.Size> supportedPreviewSizes = parameters.SupportedPreviewSizes.ToList();
            List<HWCamera.Size> supportedPictureSizes = parameters.SupportedPictureSizes.ToList();


            //Fix for getting max resolution;
            int lastIndex = supportedPictureSizes.Count - 1;
            if (supportedPictureSizes.ElementAt(0).Height < supportedPictureSizes.ElementAt(lastIndex).Height) {
                supportedPictureSizes.Reverse();//Collections.reverse(supportedPictureSizes);
            }

            List<SizePair> validPreviewSizes = new List<SizePair>();
            foreach (var previewSize in supportedPreviewSizes) {
                float previewAspectRatio = (float)previewSize.Width / (float)previewSize.Height;

                // By looping through the picture sizes in order, we favor the higher resolutions.
                // We choose the highest resolution in order to support taking the full resolution
                // picture later.
                foreach (var pictureSize in supportedPictureSizes) {
                    float pictureAspectRatio = (float)pictureSize.Width / (float)pictureSize.Height;
                    if (Math.Abs(previewAspectRatio - pictureAspectRatio) < ASPECT_RATIO_TOLERANCE) {
                        validPreviewSizes.Add(new SizePair(previewSize, pictureSize));
                        break;
                    }
                }
            }

            // If there are no picture sizes with the same aspect ratio as any preview sizes, allow all
            // of the preview sizes and hope that the camera can handle it.  Probably unlikely, but we
            // still account for it.
            if (validPreviewSizes.Count == 0) {
                Log.Warn(TAG, "No preview sizes have a corresponding same-aspect-ratio picture size");
                foreach (var previewSize in supportedPreviewSizes) {
                    // The null picture size will let us know that we shouldn't set a picture size.
                    validPreviewSizes.Add(new SizePair(previewSize, null));
                }
            }

            return validPreviewSizes;
        }

        /**
         * Selects the most suitable preview frames per second range, given the desired frames per
         * second.
         *
         * @param camera            the camera to select a frames per second range from
         * @param desiredPreviewFps the desired frames per second for the camera preview frames
         * @return the selected preview frames per second range
         */
        private int[] selectPreviewFpsRange(HWCamera camera, float desiredPreviewFps)
        {
            // The camera API uses integers scaled by a factor of 1000 instead of floating-point frame
            // rates.
            int desiredPreviewFpsScaled = (int)(desiredPreviewFps * 1000.0f);

            // The method for selecting the best range is to minimize the sum of the differences between
            // the desired value and the upper and lower bounds of the range.  This may select a range
            // that the desired value is outside of, but this is often preferred.  For example, if the
            // desired frame rate is 29.97, the range (30, 30) is probably more desirable than the
            // range (15, 30).
            int[] selectedFpsRange = null;
            int minDiff = int.MaxValue;
            List<int[]> previewFpsRangeList = camera.GetParameters().SupportedPreviewFpsRange.ToList();
            foreach (var range in previewFpsRangeList) {
                int deltaMin = desiredPreviewFpsScaled - range[(int)HWCamera.Parameters.PreviewFpsMinIndex];
                int deltaMax = desiredPreviewFpsScaled - range[(int)HWCamera.Parameters.PreviewFpsMaxIndex];
                int diff = Math.Abs(deltaMin) + Math.Abs(deltaMax);
                if (diff < minDiff) {
                    selectedFpsRange = range;
                    minDiff = diff;
                }
            }
            return selectedFpsRange;
        }

        /**
         * Calculates the correct rotation for the given camera id and sets the rotation in the
         * parameters.  It also sets the camera's display orientation and rotation.
         *
         * @param parameters the camera parameters for which to set the rotation
         * @param cameraId   the camera id to set rotation based on
         */
        private void setRotation(HWCamera camera, HWCamera.Parameters parameters, int cameraId)
        {
            //WindowManager windowManager = (WindowManager)mContext.GetSystemService(Context.WindowService);

            IWindowManager windowManager = mContext.GetSystemService(Context.WindowService).JavaCast<IWindowManager>();

            int degrees = 0;
            int rotation = (int)windowManager.DefaultDisplay.Rotation;
            switch (rotation) {
                case (int)SurfaceOrientation.Rotation0:
                    degrees = 0;
                    break;
                case (int)SurfaceOrientation.Rotation90:
                    degrees = 90;
                    break;
                case (int)SurfaceOrientation.Rotation180:
                    degrees = 180;
                    break;
                case (int)SurfaceOrientation.Rotation270:
                    degrees = 270;
                    break;

                default:
                    Log.Error(TAG, "Bad rotation value: " + rotation);
                    break;
            }

            HWCamera.CameraInfo cameraInfo = new HWCamera.CameraInfo();
            HWCamera.GetCameraInfo(cameraId, cameraInfo);

            int angle;
            int displayAngle;
            if (cameraInfo.Facing == HWCamera.CameraInfo.CameraFacingFront) {
                angle = (cameraInfo.Orientation + degrees) % 360;
                displayAngle = (360 - angle) % 360; // compensate for it being mirrored
            }
            else {  // back-facing
                angle = (cameraInfo.Orientation - degrees + 360) % 360;
                displayAngle = angle;
            }

            // This corresponds to the rotation constants in {@link Frame}.
            mRotation = angle / 90;

            camera.SetDisplayOrientation(displayAngle);
            parameters.SetRotation(angle);
        }

        /**
     * Creates one buffer for the camera preview callback.  The size of the buffer is based off of
     * the camera preview size and the format of the camera image.
     *
     * @return a new preview buffer of the appropriate size for the current camera settings
     */
        private byte[] createPreviewBuffer(Size previewSize)
        {
            int bitsPerPixel = ImageFormat.GetBitsPerPixel(ImageFormat.Nv21);
            long sizeInBits = previewSize.Height * previewSize.Width * bitsPerPixel;
            int bufferSize = (int)Math.Ceiling(sizeInBits / 8.0d) + 1;

            //
            // NOTICE: This code only works when using play services v. 8.1 or higher.
            //

            // Creating the byte array this way and wrapping it, as opposed to using .allocate(),
            // should guarantee that there will be an array to work with.
            byte[] byteArray = new byte[bufferSize];
            ByteBuffer buffer = ByteBuffer.Wrap(byteArray);
            //byte[] arr1 = buffer.ToArray<byte>();
            //var arrlen1= arr1?.Length ?? 0;
            //var arrlen2 = byteArray.Length;
            if (!buffer.HasArray ){//|| (arrlen1 != arrlen2)) {
                // I don't think that this will ever happen.  But if it does, then we wouldn't be
                // passing the preview content to the underlying detector later.
                throw new Exception("Failed to create valid buffer for camera source.");
            }

            mBytesToByteBuffer[byteArray] = buffer;
            return byteArray;
        }

        //==============================================================================================
        // Frame processing
        //==============================================================================================

        /**
         * Called when the camera has a new preview frame.
         */
        private class CameraPreviewCallback :Java.Lang.Object, HWCamera.IPreviewCallback {
            //@Override
        public void OnPreviewFrame(byte[] data, HWCamera camera)
        {
            mFrameProcessor.setNextFrame(data, camera);
        }
    }

}

    //==============================================================================================
    // Bridge Functionality for the Camera1 API
    //==============================================================================================

    /**
     * Callback interface used to signal the moment of actual image capture.
     */
    public interface IShutterCallback {
        /**
         * Called as near as possible to the moment when a photo is captured from the sensor. This
         * is a good opportunity to play a shutter sound or give other feedback of camera operation.
         * This may be some time after the photo was triggered, but some time before the actual data
         * is available.
         */
        void OnShutter();
    }

    /**
     * Callback interface used to supply image data from a photo capture.
     */
    public interface IPictureCallback {
        /**
         * Called when image data is available after a picture is taken.  The format of the data
         * is a jpeg binary.
         */
        void OnPictureTaken(byte[] data);
    }

    /**
     * Callback interface used to notify on completion of camera auto focus.
     */
    public interface AutoFocusCallback {
        /**
         * Called when the camera auto focus completes.  If the camera
         * does not support auto-focus and autoFocus is called,
         * onAutoFocus will be called immediately with a fake value of
         * <code>success</code> set to <code>true</code>.
         * <p/>
         * The auto-focus routine does not lock auto-exposure and auto-white
         * balance after it completes.
         *
         * @param success true if focus was successful, false if otherwise
         */
        void OnAutoFocus(bool success);
    }

    /**
     * Callback interface used to notify on auto focus start and stop.
     * <p/>
     * <p>This is only supported in continuous autofocus modes -- {@link
     * Camera.Parameters#FOCUS_MODE_CONTINUOUS_VIDEO} and {@link
     * Camera.Parameters#FOCUS_MODE_CONTINUOUS_PICTURE}. Applications can show
     * autofocus animation based on this.</p>
     */
    public interface AutoFocusMoveCallback {
        /**
         * Called when the camera auto focus starts or stops.
         *
         * @param start true if focus starts to move, false if focus stops to move
         */
        void OnAutoFocusMoving(bool start);
    }


    public class LockerObj : Java.Lang.Object{
    }

}


/**/