using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content.PM;
using Android.Gms.Common;
using Android.Gms.Vision;
using Android.Gms.Vision.Faces;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Util;
using static Android.Gms.Vision.MultiProcessor;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Graphics.Drawables;
using Android.Graphics;
using FaceRecognitionSimple.CustomControls.Helpers;
using System.Linq;
using System.IO;
using Environment = Android.OS.Environment;
using Path = System.IO.Path;
using System.Collections.Generic;
using FaceRecognitionSimple.CustomControls;

namespace FaceRecognitionSimple
{
	[Activity(Label = "FaceRecognitionSimple", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/Theme.AppCompat.NoActionBar")]
	public class MainActivity : AppCompatActivity, IFactory
	{
		private static readonly string TAG = "FaceTracker";

		private CameraSource mCameraSource = null;
        CameraSourceProperties CameraSourceProperties = new CameraSourceProperties() {
            DesiredPreviewSizeWidth = 640,
            DesiredPreviewSizeHeight = 480
        };

        private CameraSourcePreview mPreview;
		private GraphicOverlay mGraphicOverlay;
		private RecyclerView rv;
		private RvAdapter adapter;
		private View vwRv;

		public static string GreetingsText
		{
			get;
			set;
		}

		private static readonly int RC_HANDLE_GMS = 9001;
		// permission request codes need to be < 256
		private static readonly int RC_HANDLE_CAMERA_PERM = 2;
        private static readonly int WRITE_EXTERNAL_STORAGE_PERM = 3;

        protected async override void OnCreate(Bundle bundle)
		{
			base.OnCreate(bundle);

			// Set our view from the "main" layout resource
			SetContentView(Resource.Layout.Main);

			mPreview = FindViewById<CameraSourcePreview>(Resource.Id.preview);
			mGraphicOverlay = FindViewById<GraphicOverlay>(Resource.Id.faceOverlay);
			rv = FindViewById<RecyclerView>(Resource.Id.rv);
			vwRv = FindViewById<View>(Resource.Id.vwrv);

			ColorGradient(vwRv, true, Color.Black, Color.Transparent);
			vwRv.Visibility = ViewStates.Invisible;

			if (ActivityCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
			{
				CreateCameraSource();
				LiveCamHelper.Init();
				LiveCamHelper.GreetingsCallback = (s,w) =>
				{
					RunOnUiThread(() =>
					{
						GreetingsText = s;
						if (w == null || !w.Any()) return;
						vwRv.Visibility = ViewStates.Visible;
						if (adapter.Items == null) adapter.Items = new System.Collections.Generic.List<Worker>();
						adapter.Items.AddRange(w);
						adapter.NotifyDataSetChanged();
					});
				};
				await LiveCamHelper.RegisterFaces();
			}
			else { 
                RequestCameraPermission(); 
            }

            if (!(ActivityCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) == Permission.Granted)) {
                RequestWriteExternalPermission();
            }

            adapter = new RvAdapter();
			adapter.Items = new System.Collections.Generic.List<Worker>();
			rv.SetLayoutManager(new LinearLayoutManager(this));
			rv.SetAdapter(adapter);
		}

		protected override void OnResume()
		{
			base.OnResume();
			StartCameraSource();
		}

		protected override void OnPause()
		{
			base.OnPause();
			mPreview.Stop();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			if (mCameraSource != null)
			{
				mCameraSource.Release();
			}
		}

		private void RequestCameraPermission()
		{
			Log.Warn(TAG, "Camera permission is not granted. Requesting permission");

			var permissions = new string[] { Manifest.Permission.Camera };

			if (!ActivityCompat.ShouldShowRequestPermissionRationale(this,
					Manifest.Permission.Camera))
			{
				ActivityCompat.RequestPermissions(this, permissions, RC_HANDLE_CAMERA_PERM);
				return;
			}

			Snackbar.Make(mGraphicOverlay, Resource.String.permission_camera_rationale,
					Snackbar.LengthIndefinite)
					.SetAction(Resource.String.ok, (o) => { ActivityCompat.RequestPermissions(this, permissions, RC_HANDLE_CAMERA_PERM); })
					.Show();
		}

        private void RequestWriteExternalPermission()
        {
            Log.Warn(TAG, "External Write permission is not granted. Requesting permission");

            var permissions = new string[] { Manifest.Permission.WriteExternalStorage, Manifest.Permission.ReadExternalStorage};

            if (!ActivityCompat.ShouldShowRequestPermissionRationale(this,
                    Manifest.Permission.WriteExternalStorage)) {
                ActivityCompat.RequestPermissions(this, permissions, WRITE_EXTERNAL_STORAGE_PERM);
                return;
            }

           
        }

       

        /**
 * Creates and starts the camera.  Note that this uses a higher resolution in comparison
 * to other detection examples to enable the barcode detector to detect small barcodes
 * at long distances.
 */
        private void CreateCameraSource()
		{

			var context = Application.Context;
			FaceDetector detector = new FaceDetector.Builder(context)
					.SetClassificationType(ClassificationType.All)
					.Build();

			detector.SetProcessor(
					new MultiProcessor.Builder(this)
							.Build());

			if (!detector.IsOperational)
			{
				// Note: The first time that an app using face API is installed on a device, GMS will
				// download a native library to the device in order to do detection.  Usually this
				// completes before the app is run for the first time.  But if that download has not yet
				// completed, then the above call will not detect any faces.
				//
				// isOperational() can be used to check if the required native library is currently
				// available.  The detector will automatically become operational once the library
				// download completes on device.
				Log.Warn(TAG, "Face detector dependencies are not yet available.");
			}

            mCameraSource = new CameraSource.Builder(context, detector)
					.SetRequestedPreviewSize(CameraSourceProperties.DesiredPreviewSizeWidth, CameraSourceProperties.DesiredPreviewSizeHeight)
					.SetFacing(CameraFacing.Front)
                    .SetAutoFocusEnabled(true)
					.SetRequestedFps(30.0f)
					.Build();

        }



        /**
         * Starts or restarts the camera source, if it exists.  If the camera source doesn't exist yet
         * (e.g., because onResume was called before the camera source was created), this will be called
         * again when the camera source is created.
         */
        private void StartCameraSource()
		{

			// check that the device has play services available.
			int code = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(
					this.ApplicationContext);
			if (code != ConnectionResult.Success)
			{
				Dialog dlg =
						GoogleApiAvailability.Instance.GetErrorDialog(this, code, RC_HANDLE_GMS);
				dlg.Show();
			}

			if (mCameraSource != null)
			{
				try
				{
					mPreview.Start(mCameraSource,CameraSourceProperties, mGraphicOverlay);
				}
				catch (System.Exception e)
				{
					Log.Error(TAG, "Unable to start camera source.", e);
					mCameraSource.Release();
					mCameraSource = null;
				}
			}
		}
		public Tracker Create(Java.Lang.Object item)
		{
			return new GraphicFaceTracker(mGraphicOverlay, mCameraSource);
		}


		public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
		{
			if (requestCode == RC_HANDLE_CAMERA_PERM)
			{
                //Log.Debug(TAG, "Got unexpected permission result: " + requestCode);
                //base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

                //return;
                if (grantResults.Length != 0 && grantResults[0] == Permission.Granted) {
                    Log.Debug(TAG, "Camera permission granted - initialize the camera source");
                    // we have permission, so create the camerasource
                    CreateCameraSource();
                    return;
                }
                else {

                    Log.Error(TAG, "Permission not granted: results len = " + grantResults.Length +
                    " Result code = " + (grantResults.Length > 0 ? grantResults[0].ToString() : "(empty)"));

                    var builder = new Android.Support.V7.App.AlertDialog.Builder(this);
                    builder.SetTitle("LiveCam")
                            .SetMessage(Resource.String.no_camera_permission)
                            .SetPositiveButton(Resource.String.ok, (o, e) => Finish())
                            .Show();
                }
            }
            //if (requestCode == WRITE_EXTERNAL_STORAGE_PERM) {


            //}

        }

		public void ColorGradient(View vw, bool isHorizontal, params int[] colors)
		{
			var drawable = new GradientDrawable();
			drawable.SetColors(colors);
			drawable.SetOrientation(
				isHorizontal ?
				GradientDrawable.Orientation.LeftRight :
				GradientDrawable.Orientation.TopBottom);
			vw.Background = drawable;
		}
	}


	class GraphicFaceTracker : Tracker, CameraSource.IPictureCallback {

		private GraphicOverlay mOverlay;
		private FaceGraphic mFaceGraphic;
		private CameraSource mCameraSource = null;
		private bool isProcessing = false;

		public GraphicFaceTracker(GraphicOverlay overlay, CameraSource cameraSource = null)
		{
			mOverlay = overlay;
			mFaceGraphic = new FaceGraphic(overlay);
			mCameraSource = cameraSource;
		}

		public override void OnNewItem(int id, Java.Lang.Object item)
		{
            lock (lockobject) {
                if (isProcessing)
                    return;
            }

            try
			{
				mFaceGraphic.SetId(id);
				if (mCameraSource != null && !isProcessing)
					mCameraSource.TakePicture(null, this);
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex.Message);
			}
		}

		public override void OnUpdate(Detector.Detections detections, Java.Lang.Object item)
		{
			var face = item as Face;
			mOverlay.Add(mFaceGraphic);
			mFaceGraphic.UpdateFace(face);
		}

		public override void OnMissing(Detector.Detections detections)
		{
			mOverlay.Remove(mFaceGraphic);

		}

		public override void OnDone()
		{
			mOverlay.Remove(mFaceGraphic);

		}

        public static object lockobject = new object();

		public void OnPictureTaken(byte[] data)
		{

            BitmapFactory.Options options = new BitmapFactory.Options();
            options.InSampleSize = 1;

            //options.inJustDecodeBounds = true;
            //BitmapFactory.DecodeByteArray(res, resId, options);
            Bitmap bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);

            byte[] bitmapData;
            using (var stream = new MemoryStream()) {
                bitmap.Compress(Bitmap.CompressFormat.Png, 0, stream);
                bitmapData = stream.ToArray();
            }

            WriteToFile(bitmapData);
            //WriteToFile(data);

            lock (lockobject){
                if (isProcessing)
                    return;

                isProcessing = true;
            }

            Task.Run(async () =>
			{
				try
				{
					Console.WriteLine("face detected: ");

					var imageAnalyzer = new ImageAnalyzer(bitmapData);
					await LiveCamHelper.ProcessCameraCapture(imageAnalyzer);

				}

				finally
				{
					isProcessing = false;
				}

			});
		}


        void WriteToFile(byte[] localdata)
        {

            string encoded = Base64.EncodeToString(localdata, Base64Flags.Default);



            var documentsPath = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDocuments);

            if (!System.IO.Directory.Exists(documentsPath.ToString())) {
                    Directory.CreateDirectory(documentsPath.ToString());
                }

            var filePath = Path.Combine(documentsPath.AbsolutePath, "image" + DateTime.Now.Ticks);

            // In this line where i create FileStream i get an Exception
            FileStream fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using (var streamWriter = new StreamWriter(fileStream)) {
                streamWriter.Write(encoded);
            }

        }

	}
}

