﻿namespace ExBuddy.OrderBotTags.Gather.GatherSpots
{
	using Buddy.Coroutines;
	using Clio.Utilities;
	using Clio.XmlEngine;
	using ExBuddy.Helpers;
	using ff14bot;
	using System.ComponentModel;
	using System.Threading.Tasks;
	using ff14bot.Behavior;
	using ff14bot.Managers;

    [XmlElement("StealthApproachGatherSpot")]
	public class StealthApproachGatherSpot : GatherSpot
	{
		[DefaultValue(true)]
		[XmlAttribute("ReturnToStealthLocation")]
		[XmlAttribute("ReturnToApproachLocation")]
		public bool ReturnToStealthLocation { get; set; }

	    [XmlAttribute("StealthLocation")]
	    [XmlAttribute("ApproachLocation")]
        public Vector3 StealthLocation { get; set; }

		[XmlAttribute("UnstealthAfter")]
		public bool UnstealthAfter { get; set; }

		public override async Task<bool> MoveFromSpot(ExGatherTag tag)
		{
			tag.StatusText = "Moving from " + this;

			var result = true;
			if (ReturnToStealthLocation)
			{
				result &= await StealthLocation.MoveToNoMount(UseMesh, tag.Radius, tag.Node.EnglishName, tag.MovementStopCallback);
			}

			if (UnstealthAfter && Core.Player.HasAura((int)AbilityAura.Stealth))
			{
				result &= await tag.CastAura(Ability.Stealth);
			}

			return result;
		}

		public override async Task<bool> MoveToSpot(ExGatherTag tag)
		{
			tag.StatusText = "Moving to " + this;

			if (StealthLocation == Vector3.Zero)
			{
				return false;
			}

			var result =
				await
					StealthLocation.MoveTo(
						UseMesh,
						radius: tag.Radius,
						name: "Stealth Location",
						stopCallback: tag.MovementStopCallback,
						dismountAtDestination: true);

		    if (!result) return false;

		    var landed = MovementManager.IsDiving || await CommonTasks.Land();
		    if (landed)
		        ActionManager.Dismount();

            await Coroutine.Yield();
		    await tag.CastAura(Ability.Stealth, AbilityAura.Stealth);

		    result = await NodeLocation.MoveToOnGroundNoMount(tag.Distance, tag.Node.EnglishName);

		    return result;
		}

		public override string ToString()
		{
			return this.DynamicToString("UnstealthAfter");
		}
	}
}