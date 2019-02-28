using System;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using FaceRecognitionSimple.CustomControls.Helpers;
using FFImageLoading;
using FFImageLoading.Views;
namespace FaceRecognitionSimple
{
	public class RvViewHolder : RecyclerView.ViewHolder
	{
		public TextView LblName { get; private set; }
		public TextView LblJob { get; private set; }
		public TextView LblTeam { get; private set; }
		public ImageViewAsync Img { get; private set; }

		public RvViewHolder(View mainview) : base(mainview)
		{
			LblName = mainview.FindViewById<TextView>(Resource.Id.LblName);
			LblJob = mainview.FindViewById<TextView>(Resource.Id.LblJob);
			LblTeam = mainview.FindViewById<TextView>(Resource.Id.LblTeam);
			Img = mainview.FindViewById<ImageViewAsync>(Resource.Id.img);
		}

		public void UpdateView(Worker worker)
		{
			LblName.Text = worker.Name;
			LblJob.Text = worker.Job.ToString();
			LblTeam.Text = worker.Team.ToString();
			ImageService.Instance.LoadUrl(worker.Image)
					.Retry(3, 200)
					.IntoAsync(Img);
		}
	}
}
