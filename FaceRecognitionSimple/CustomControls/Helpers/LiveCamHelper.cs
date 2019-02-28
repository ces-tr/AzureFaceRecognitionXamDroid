
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ProjectOxford.Face;
using FaceRecognitionSimple.CustomControls.Helpers;
using System.Collections.Generic;

namespace FaceRecognitionSimple
{
	public static class LiveCamHelper
	{
		public static bool IsFaceRegistered { get; set; }

		public static bool IsInitialized { get; set; }

		public static string WorkspaceKey
		{
			get;
			set;
		}
		public static Action<string, List<Worker>> GreetingsCallback { get => greetingsCallback; set => greetingsCallback = value; }

		private static Action<string, List<Worker>> greetingsCallback;

		public static void Init(Action throttled = null)
		{
			FaceServiceHelper.ApiKey = "31f6feb7031f4d538409fb3074fcebf0";
			if (throttled != null)
				FaceServiceHelper.Throttled += throttled;

			WorkspaceKey = Guid.NewGuid().ToString();
			ImageAnalyzer.PeopleGroupsUserDataFilter = WorkspaceKey;
			FaceListManager.FaceListsUserDataFilter = WorkspaceKey;

			IsInitialized = true;
		}

		//public static async Task RegisterFaces()
		//{

		//	try
		//	{
		//		var persongroupId = Guid.NewGuid().ToString();
		//		await FaceServiceHelper.CreatePersonGroupAsync(persongroupId,
		//												"Xamarin",
		//											 WorkspaceKey);
		//		await FaceServiceHelper.CreatePersonAsync(persongroupId, "Ella");

		//		var personsInGroup = await FaceServiceHelper.GetPersonsAsync(persongroupId);

		//		await FaceServiceHelper.AddPersonFaceAsync(persongroupId, personsInGroup[0].PersonId,
		//												   "https://scontent.fgdl4-1.fna.fbcdn.net/v/t1.0-9/17424649_440960489578442_3015047743451238844_n.jpg?_nc_cat=100&_nc_ht=scontent.fgdl4-1.fna&oh=c97a21eaf2cab9d1395dc18cb7c672b2&oe=5C9C0F2D",
		//			 null, null);

		//		await FaceServiceHelper.AddPersonFaceAsync(persongroupId, personsInGroup[0].PersonId,
		//								   "https://scontent.fgdl4-1.fna.fbcdn.net/v/t1.0-9/13165852_277075955966897_6949182136400937330_n.jpg?_nc_cat=107&_nc_ht=scontent.fgdl4-1.fna&oh=eb2421d9a1e4997eeb9a8dd10f93c388&oe=5C91E5C6",
		//			 null, null);

		//		await FaceServiceHelper.TrainPersonGroupAsync(persongroupId);

		//		IsFaceRegistered = true;


		//	}
		//	catch (FaceAPIException ex)

		//	{
		//		Console.WriteLine(ex.Message);
		//		IsFaceRegistered = false;

		//	}

		//}

		public static async Task RegisterFaces()
		{

			try
			{

				var persongroupId = Guid.NewGuid().ToString();
				await FaceServiceHelper.CreatePersonGroupAsync(persongroupId,
														"Xamarin",
													 WorkspaceKey);

				foreach (var item in Workers.WORKES)
				{
					var person = await FaceServiceHelper.CreatePersonAsync(persongroupId, item.Name);

					item.IdFR = person.PersonId;

					await FaceServiceHelper.AddPersonFaceAsync(persongroupId, person.PersonId,
											   item.Image, null, null);
				}

				await FaceServiceHelper.TrainPersonGroupAsync(persongroupId);
				IsFaceRegistered = true;
			}
			catch (FaceAPIException ex)

			{
				Console.WriteLine(ex.Message);
				IsFaceRegistered = false;

			}

		}

		public static async Task ProcessCameraCapture(ImageAnalyzer e)
		{

			DateTime start = DateTime.Now;

			await e.DetectFacesAsync();

			if (e.DetectedFaces.Any())
			{
				await e.IdentifyFacesAsync();
				string greetingsText = GetGreettingFromFaces(e);

				if (e.IdentifiedPersons.Any())
				{
					List<IdentifiedPerson> temp = e.IdentifiedPersons.ToList();

					var tempPerson = new List<Worker>();
					foreach (var item in temp)
					{
						var personFound = Workers.WORKES.Where(p => p.IdFR == item.Person.PersonId).ToList();
						if (personFound.Any())
						{
							tempPerson.Add(personFound[0]);
						}
					}

					if (greetingsCallback != null)
					{
						DisplayMessage(greetingsText, tempPerson);
					}

					Console.WriteLine(greetingsText);
				}
				else
				{
					DisplayMessage("No Idea, who you're.. Register your face.",null);

					Console.WriteLine("No Idea");

				}
			}
			else
			{
				// DisplayMessage("No face detected.");

				Console.WriteLine("No Face ");

			}

			TimeSpan latency = DateTime.Now - start;
			var latencyString = string.Format("Face API latency: {0}ms", (int)latency.TotalMilliseconds);
			Console.WriteLine(latencyString);
		}

		private static string GetGreettingFromFaces(ImageAnalyzer img)
		{
			if (img.IdentifiedPersons.Any())
			{
				string names = img.IdentifiedPersons.Count() > 1 ? string.Join(", ", img.IdentifiedPersons.Select(p => p.Person.Name)) : img.IdentifiedPersons.First().Person.Name;

				if (img.DetectedFaces.Count() > img.IdentifiedPersons.Count())
				{
					return string.Format("Welcome back, {0} and company!", names);
				}
				else
				{
					return string.Format("Welcome back, {0}!", names);
				}
			}
			else
			{
				if (img.DetectedFaces.Count() > 1)
				{
					return "Hi everyone! If I knew any of you by name I would say it...";
				}
				else
				{
					return "Hi there! If I knew you by name I would say it...";
				}
			}
		}

		static void DisplayMessage(string greetingsText, List<Worker> workers)
		{
			greetingsCallback?.Invoke(greetingsText, workers);
		}
	}
}
