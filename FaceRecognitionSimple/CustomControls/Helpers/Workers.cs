using System;
using System.Collections.Generic;
namespace FaceRecognitionSimple.CustomControls.Helpers
{
	public static class Workers
	{

		public static List<Worker> WORKES = new List<Worker> {
			new Worker{
			Id = "1",
			Name = "Jaime",
			Job = JobEnum.Pet,
			Team = TeamEnum.PetStore,
			Image = "https://i.imgur.com/F680y8K.jpg"
			//Image = "https://scontent.fgdl4-1.fna.fbcdn.net/v/t1.0-9/13165852_277075955966897_6949182136400937330_n.jpg?_nc_cat=107&_nc_ht=scontent.fgdl4-1.fna&oh=eb2421d9a1e4997eeb9a8dd10f93c388&oe=5C91E5C6"
			},
			new Worker{
			Id = "2",
			Name = "Josue",
			Job = JobEnum.SoftwareSpacialist,
			Team = TeamEnum.XamarinTeam,
			Image = "https://i.imgur.com/8frzyzs.jpg"
			},
			new Worker{
			Id = "3",
			Name = "Chuy",
			Job = JobEnum.SoftwareSpacialist,
			Team = TeamEnum.XamarinTeam,
			Image = "https://i.imgur.com/kvGJomM.jpg"
			},
            new Worker{
                Id="4",
                Name="Cesar",
                Job = JobEnum.SoftwareSpacialist,
                Team = TeamEnum.XamarinTeam,
                Image= "https://i.imgur.com/0rwdPEM.jpg"
            }
        };
	}

	public class Worker
	{
		public string Id { get; set; }
		public Guid IdFR { get; set; }
		public string Name { get; set; }
		public JobEnum Job { get; set; }
		public TeamEnum Team { get; set; }
		public string Image { get; set; }
	}

	public enum JobEnum
	{
		SoftwareSpacialist,
		TechnicalLead,
		Pet
	}

	public enum TeamEnum
	{
		XamarinTeam,
		PetStore
	}
}
