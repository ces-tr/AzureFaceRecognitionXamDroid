/*
 * Ported to C# from https://github.com/googlesamples/android-vision/tree/master/visionSamples/FaceTracker
 * Ported by Nish Anil (Nish@microsoft.com)
 * Copyright (C) The Android Open Source Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Util;
using Android.Gms.Vision;
using Android.Graphics;
using Android.Content.Res;
using Android.Hardware;
using Camera = Android.Hardware.Camera;
using FaceRecognitionSimple.CustomControls;

namespace FaceRecognitionSimple
{
	public sealed class CameraSourcePreview : ViewGroup, ISurfaceHolderCallback
	{

        private Dictionary<Android.Hardware.CameraFacing, Android.Gms.Vision.CameraFacing> CameraFacingMapping =
                    new Dictionary<Android.Hardware.CameraFacing, Android.Gms.Vision.CameraFacing>() {
                        {Android.Hardware.CameraFacing.Back, Android.Gms.Vision.CameraFacing.Back},
                        {Android.Hardware.CameraFacing.Front, Android.Gms.Vision.CameraFacing.Front}
                    };

        private static readonly String TAG = "CameraSourcePreview";

		private Context mContext;
		private SurfaceView mSurfaceView;
		private bool mStartRequested;
		private bool mSurfaceAvailable;
		private CameraSource mCameraSource;

		private GraphicOverlay mOverlay;
        private CameraSourceProperties cameraSourceProperties;

        public CameraSourcePreview(Context context, IAttributeSet attrs) : base(context, attrs)
		{
			mContext = context;
			mStartRequested = false;
			mSurfaceAvailable = false;

			mSurfaceView = new SurfaceView(context);
			mSurfaceView.Holder.AddCallback(this);

			AddView(mSurfaceView);
		}

		public void Start(CameraSource cameraSource)
		{
			if (cameraSource == null)
			{
				Stop();
			}

			mCameraSource = cameraSource;

			if (mCameraSource != null)
			{
				mStartRequested = true;
				StartIfReady();
			}
		}

		public void Start(CameraSource cameraSource, CameraSourceProperties cameraSourceProperties, GraphicOverlay overlay)
		{
			mOverlay = overlay;
            this.cameraSourceProperties = cameraSourceProperties;

            Start(cameraSource);
		}

		public void Stop()
		{
			if (mCameraSource != null)
			{
				mCameraSource.Stop();
			}
		}

		public void Release()
		{
			if (mCameraSource != null)
			{
				mCameraSource.Release();
				mCameraSource = null;
			}
		}

		private void StartIfReady()
		{
			if (mStartRequested && mSurfaceAvailable)
			{

                mCameraSource.Start(mSurfaceView.Holder);
                Camera camera = mCameraSource.GetCamera();

                if (mOverlay != null)
				{
					var size = mCameraSource.PreviewSize;
					var min = Math.Min(size.Width, size.Height);
					var max = Math.Max(size.Width, size.Height);

                    int numCameras = Camera.NumberOfCameras;
                    Camera.Size optimalSize = null;
                    for (int i = 0; i < numCameras; i++) {

                        var cameraInfo = new Camera.CameraInfo();
                         Camera.GetCameraInfo(i, cameraInfo);

                        Android.Gms.Vision.CameraFacing visionFacing = CameraFacingMapping.FirstOrDefault(hcf => hcf.Key.Equals(cameraInfo.Facing)).Value;

                        if (visionFacing.Equals( mCameraSource.CameraFacing)) {
                            //Camera camera = Camera.Open(i);
                            Camera.Parameters cameraParams = camera.GetParameters();
                            //Java.Util.ArrayList supportedpreviews= 
                            List<Camera.Size> sizes = (List<Camera.Size>)cameraParams.SupportedPreviewSizes.ToList();

                            optimalSize = getOptimalPreviewSize(sizes, cameraSourceProperties.DesiredPreviewSizeWidth, cameraSourceProperties.DesiredPreviewSizeHeight);

                            cameraParams.SetPreviewSize(optimalSize.Width, optimalSize.Height);
                            cameraParams.SetPictureSize(optimalSize.Width, optimalSize.Height);

                            camera.SetParameters(cameraParams);
                            break;
                        }
                    }

                    if(optimalSize != null) {
                        min = optimalSize.Width;
                        max = optimalSize.Height;
                    }


                    if (IsPortraitMode())
					{
						// Swap width and height sizes when in portrait, since it will be rotated by
						// 90 degrees
						mOverlay.SetCameraInfo(min, max, mCameraSource.CameraFacing);

					}
					else
					{
						mOverlay.SetCameraInfo(max, min, mCameraSource.CameraFacing);
					}
					mOverlay.Clear();
				}
				mStartRequested = false;
			}
		}

        private Camera.Size getOptimalPreviewSize(List<Camera.Size> sizes, int w, int h)
        {
            double ASPECT_TOLERANCE = 0.1;
            double targetRatio = (double)w / h;
            if (sizes == null) return null;
            Camera.Size optimalSize = null;
            double minDiff = Double.MaxValue;
            int targetHeight = h;
            // Try to find an size match aspect ratio and size
            foreach (var size in sizes) {
                double ratio = (double)size.Width / size.Height;
                if (Math.Abs(ratio - targetRatio) > ASPECT_TOLERANCE) continue;
                if (Math.Abs(size.Height - targetHeight) < minDiff) {
                    optimalSize = size;
                    minDiff = Math.Abs(size.Height - targetHeight);
                }
            }
            // Cannot find the one match the aspect ratio, ignore the requirement
            if (optimalSize == null) {
                minDiff = Double.MaxValue;
                foreach (var size in sizes) {
                    if (Math.Abs(size.Height - targetHeight) < minDiff) {
                        optimalSize = size;
                        minDiff = Math.Abs(size.Height - targetHeight);
                    }
                }
            }
            return optimalSize;
        }

        private bool IsPortraitMode()
		{
			var orientation = mContext.Resources.Configuration.Orientation;
			if (orientation == Android.Content.Res.Orientation.Landscape)
			{
				return false;
			}
			if (orientation == Android.Content.Res.Orientation.Portrait)
			{
				return true;
			}

			Log.Debug(TAG, "isPortraitMode returning false by default");
			return false;
		}

		public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height)
		{

		}

		public void SurfaceCreated(ISurfaceHolder holder)
		{
			mSurfaceAvailable = true;


			try
			{
				StartIfReady();
			}
			catch (Exception e)
			{
				Log.Error(TAG, "Could not start camera source.", e);
			}
		}

		public void SurfaceDestroyed(ISurfaceHolder holder)
		{
			mSurfaceAvailable = false;
		}

		protected override void OnLayout(bool changed, int l, int t, int r, int b)
		{
			int width = 320;
			int height = 240;
			if (mCameraSource != null)
			{
				var size = mCameraSource.PreviewSize;
				if (size != null)
				{
					width = size.Width;
					height = size.Height;
				}
			}

			// Swap width and height sizes when in portrait, since it will be rotated 90 degrees
			if (IsPortraitMode())
			{
				int tmp = width;
				width = height;
				height = tmp;
			}

			int layoutWidth = r - l;
			int layoutHeight = b - t;

			// Computes height and width for potentially doing fit width.
			int childWidth = layoutWidth;
			int childHeight = (int)(((float)layoutWidth / (float)width) * height);

			// If height is too tall using fit width, does fit height instead.
			if (childHeight > layoutHeight)
			{
				childHeight = layoutHeight;
				childWidth = (int)(((float)layoutHeight / (float)height) * width);
			}

			for (int i = 0; i < ChildCount; ++i)
			{

				GetChildAt(i).Layout(0, 0, childWidth, childHeight);
			}

			try
			{
				StartIfReady();
			}
			catch (Exception e)
			{
				Log.Error(TAG, "Could not start camera source.", e);
			}
		}
	}



}