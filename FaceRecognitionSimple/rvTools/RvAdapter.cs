using System;
using System.Collections.Generic;
using Android.Support.V7.Widget;
using Android.Views;
using Java.Nio.FileNio;
using FaceRecognitionSimple.CustomControls.Helpers;

namespace FaceRecognitionSimple
{
	public class RvAdapter : RecyclerView.Adapter
	{
		public List<Worker> Items;

		public override int ItemCount => Items.Count;

		public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
		{
			var vh = holder as RvViewHolder;
			vh.UpdateView(Items[position]);
		}

		public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
		{
			var vh = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.rvcell, parent, false);
			return new RvViewHolder(vh);
		}
	}
}
