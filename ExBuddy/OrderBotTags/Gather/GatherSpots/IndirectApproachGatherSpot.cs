﻿namespace ExBuddy.OrderBotTags.Gather.GatherSpots
{
	using Buddy.Coroutines;
	using Clio.Utilities;
	using Clio.XmlEngine;
	using ExBuddy.Helpers;
	using System.ComponentModel;
	using System.Threading.Tasks;
	using ff14bot.Behavior;
	using ff14bot.Managers;

    [XmlElement("IndirectApproachGatherSpot")]
	public class IndirectApproachGatherSpot : GatherSpot
	{
		[XmlAttribute("ApproachLocation")]
		public Vector3 ApproachLocation { get; set; }

		[DefaultValue(true)]
		[XmlAttribute("ReturnToApproachLocation")]
		public bool ReturnToApproachLocation { get; set; }

		public override async Task<bool> MoveFromSpot(ExGatherTag tag)
		{
			tag.StatusText = "Moving from " + this;

			var result = true;
			if (ReturnToApproachLocation)
			{
				result &= await ApproachLocation.MoveToOnGroundNoMount(tag.Radius, tag.Node.EnglishName);
			}

			return result;
		}

		public override async Task<bool> MoveToSpot(ExGatherTag tag)
		{
			tag.StatusText = "Moving to " + this;

			if (ApproachLocation == Vector3.Zero)
			{
				return false;
			}

			var result =
				await
					ApproachLocation.MoveTo(
						UseMesh,
						radius: tag.Radius,
						name: "Approach Location",
						stopCallback: tag.MovementStopCallback);

		    if (!result) return false;

		    var landed = MovementManager.IsDiving || await CommonTasks.Land();
		    if (landed)
		        ActionManager.Dismount();

		    await Coroutine.Yield();
		    result = await NodeLocation.MoveToOnGroundNoMount(tag.Distance, tag.Node.EnglishName);

		    return result;
		}
	}
}