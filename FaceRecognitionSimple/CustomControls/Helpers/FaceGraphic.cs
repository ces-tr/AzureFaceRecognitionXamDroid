using Android.Gms.Vision.Faces;
using Android.Graphics;

namespace FaceRecognitionSimple
{
	public class FaceGraphic : Graphic
	{
		private static readonly float FACE_POSITION_RADIUS = 10.0f;
		private static readonly float ID_TEXT_SIZE = 40.0f;
		private static readonly float ID_Y_OFFSET = 10.0f;
		private static readonly float ID_X_OFFSET = 0.0f;
		private static readonly float BOX_STROKE_WIDTH = 5.0f;

		private static Color[] COLOR_CHOICES = {
		Color.Blue,
		Color.Cyan,
		Color.Green,
		Color.Magenta,
		Color.Red,
		Color.White,
		Color.Yellow
	};
		private static int mCurrentColorIndex = 0;
		private bool initFlag = true;

		private Paint mFacePositionPaint;
		private Paint mIdPaint;
		private Paint mBoxPaint;

		private volatile Face mFace;
		private int mFaceId;
		private float mFaceHappiness;

		public FaceGraphic(GraphicOverlay overlay) : base(overlay)
		{
			mCurrentColorIndex = (mCurrentColorIndex + 1) % COLOR_CHOICES.Length;
			var selectedColor = COLOR_CHOICES[mCurrentColorIndex];

			mFacePositionPaint = new Paint()
			{
				Color = selectedColor
			};
			mIdPaint = new Paint()
			{
				Color = selectedColor,
				TextSize = ID_TEXT_SIZE
			};
			mBoxPaint = new Paint()
			{
				Color = selectedColor
			};
			mBoxPaint.SetStyle(Paint.Style.Stroke);
			mBoxPaint.StrokeWidth = BOX_STROKE_WIDTH;
		}

		public void SetId(int id)
		{
			mFaceId = id;
		}


		/**
         * Updates the face instance from the detection of the most recent frame.  Invalidates the
         * relevant portions of the overlay to trigger a redraw.
         */
		public void UpdateFace(Face face)
		{
			mFace = face;
			PostInvalidate();
		}

		public override void Draw(Canvas canvas)
		{
			Face face = mFace;
			if (face == null)
			{
				return;
			}

			// Draws a circle at the position of the detected face, with the face's track id below.
			float x = TranslateX(face.Position.X + face.Width / 2);
			float y = TranslateY(face.Position.Y + face.Height / 2);

			//HACK: Demo only
			if (initFlag)
			{
				MainActivity.GreetingsText = string.Empty;
				initFlag = false;
			}

			// Draws a bounding box around the face.
			float xOffset = ScaleX(face.Width / 2.0f);
			float yOffset = ScaleY(face.Height / 2.0f);
			float left = x - xOffset;
			float top = y - yOffset;
			float right = x + xOffset;
			float bottom = y + yOffset;

			if (!string.IsNullOrEmpty(MainActivity.GreetingsText))
				canvas.DrawText(MainActivity.GreetingsText, x - xOffset, y - yOffset - 20, mIdPaint);

			canvas.DrawRect(left, top, right, bottom, mBoxPaint);
		}
	}
}