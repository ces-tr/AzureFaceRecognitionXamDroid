using System;
using Android.Gms.Vision;
using Android.Hardware;
using Android.Runtime;

namespace FaceRecognitionSimple {

    public static class CameraSourceExtensions {

        //HACK get camera object from google vision camerasource implementation 
        public static Camera GetCamera(this CameraSource cameraSource)
        {
            var javaHero = cameraSource.JavaCast<Java.Lang.Object>();
            var fields = javaHero.Class.GetDeclaredFields();
            foreach (var field in fields) {
                if (field.Type.CanonicalName.Equals("android.hardware.camera", StringComparison.OrdinalIgnoreCase)) {
                    field.Accessible = true;
                    var camera = field.Get(javaHero);
                    var cCamera = (Android.Hardware.Camera)camera;
                    return cCamera;
                }
            }

            return null;
        }
    }
}
