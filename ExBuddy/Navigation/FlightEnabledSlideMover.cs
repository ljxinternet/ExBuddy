﻿namespace ExBuddy.Navigation
{
	using System;
	using System.Diagnostics;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows.Media;

	using Buddy.Coroutines;

	using Clio.Utilities;

	using ExBuddy.Attributes;
	using ExBuddy.Helpers;
	using ExBuddy.Interfaces;
	using ExBuddy.Logging;

	using ff14bot;
	using ff14bot.Behavior;
	using ff14bot.Interfaces;
	using ff14bot.Managers;
	using ff14bot.Navigation;
	using ff14bot.NeoProfiles;
	using ff14bot.Settings;

	[LoggerName("FlightMover")]
	public class FlightEnabledSlideMover : LogColors, IFlightEnabledPlayerMover
	{
		private static Func<Vector3, bool> shouldFlyToFunc = ShouldFlyInternal;

		protected readonly Logger Logger;

		internal bool IsMovingTowardsLocation;

		private readonly IFlightMovementArgs flightMovementArgs;

		private readonly IPlayerMover innerMover;

		private readonly Stopwatch landingStopwatch = new Stopwatch();

		private readonly Stopwatch takeoffStopwatch = new Stopwatch();

		private readonly Stopwatch totalLandingStopwatch = new Stopwatch();

		private Coroutine coroutine;

		private bool disposed;

		private Coroutine landingCoroutine;

		private Task landingTask;

		private Task takeoffTask;

		private Vector3 lastDestination;

		public FlightEnabledSlideMover(IPlayerMover innerMover, bool forceLanding = false)
			: this(innerMover, new FlightMovementArgs { ForceLanding = forceLanding }) {}

		public FlightEnabledSlideMover(IPlayerMover innerMover, IFlightMovementArgs flightMovementArgs)
		{
			if (flightMovementArgs == null)
			{
				throw new NullReferenceException("flightMovementArgs is null");
			}

			Logger = new Logger(this);
			this.innerMover = innerMover;
			Navigator.PlayerMover = this;
			this.flightMovementArgs = flightMovementArgs;

			GameEvents.OnMapChanged += GameEventsOnMapChanged;
		}

		public override Color Info
		{
			get
			{
				return Colors.LightSkyBlue;
			}
		}

		public IPlayerMover InnerMover
		{
			get
			{
				return innerMover;
			}
		}

		protected internal bool ShouldFly { get; private set; }

		#region IDisposable Members

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				Navigator.PlayerMover = innerMover;
				GameEvents.OnMapChanged -= GameEventsOnMapChanged;
			}
		}

		#endregion

		#region IFlightEnabledPlayerMover Members

		public bool CanFly
		{
			get
			{
				return WorldManager.CanFly;
			}
		}

		public IFlightMovementArgs FlightMovementArgs
		{
			get
			{
				return flightMovementArgs;
			}
		}

		public bool IsLanding { get; protected set; }

		public bool IsTakingOff { get; protected set; }

		public async Task SetShouldFlyAsync(Task<Func<Vector3, bool>> customShouldFlyToFunc)
		{
			shouldFlyToFunc = await customShouldFlyToFunc;
		}

		public bool ShouldFlyTo(Vector3 destination)
		{
			if (shouldFlyToFunc == null)
			{
				return false;
			}

			return CanFly && (ShouldFly = shouldFlyToFunc(destination));
		}

		#endregion

		#region IPlayerMover Members

		public void MoveStop()
		{
			if (!IsLanding)
			{
				innerMover.MoveStop();
				IsMovingTowardsLocation = false;
			}

			// TODO: Check can land!!
			if (!IsLanding && (flightMovementArgs.ForceLanding || GameObjectManager.LocalPlayer.Location.IsGround(4.5f)))
			{
				IsLanding = true;
				ForceLanding();
			}
		}

		public void MoveTowards(Vector3 location)
		{

			if (ShouldFly && !MovementManager.IsFlying && !IsTakingOff)
			{
				IsTakingOff = true;
				EnsureFlying();
			}

			if (!IsTakingOff)
			{
				lastDestination = location;
				IsMovingTowardsLocation = true;
				innerMover.MoveTowards(location);
			}
		}

		#endregion

		public void EnsureFlying()
		{
			if (!MovementManager.IsFlying && Actionmanager.CanMount == 0)
			{
				if (!takeoffStopwatch.IsRunning)
				{
					takeoffStopwatch.Restart();
				}

				if (takeoffTask == null)
				{
					Logger.Info("Started Takeoff Task");
					takeoffTask = Task.Factory.StartNew(
						() =>
							{
								try
								{
									while (!MovementManager.IsFlying && Behaviors.ShouldContinue)
									{
										if (takeoffStopwatch.ElapsedMilliseconds > 10000)
										{
											Logger.Error("Takeoff failed. Passing back control.");
											innerMover.MoveStop();
											IsTakingOff = false;
											return;
										}

										if (coroutine == null || coroutine.IsFinished)
										{
											Logger.Verbose("Created new Takeoff Coroutine");
											coroutine = new Coroutine(() => CommonTasks.TakeOff());
										}

										if (!coroutine.IsFinished && !MovementManager.IsFlying && Behaviors.ShouldContinue)
										{
											Logger.Verbose("Resumed Takeoff Coroutine");
											coroutine.Resume();
										}

										Thread.Sleep(33);
									}
								}
								finally
								{
									if (IsTakingOff)
									{
										Logger.Info("Takeoff took {0} ms or less", takeoffStopwatch.Elapsed);
									}

									takeoffStopwatch.Reset();
									IsTakingOff = false;
									takeoffTask = null;
								}
							});
				}
			}
			else
			{
				IsTakingOff = false;
			}
		}

		public void ForceLanding()
		{
			if (MovementManager.IsFlying)
			{
				if (!landingStopwatch.IsRunning)
				{
					landingStopwatch.Restart();
				}

				if (!totalLandingStopwatch.IsRunning)
				{
					totalLandingStopwatch.Restart();
				}

				if (landingTask == null)
				{
					Logger.Info("Started Landing Task");
					landingTask = Task.Factory.StartNew(
						() =>
							{
								try
								{
									while (MovementManager.IsFlying && Behaviors.ShouldContinue && !IsMovingTowardsLocation)
									{
										if (landingStopwatch.ElapsedMilliseconds < 2000)
										{
											// TODO: possible check to see if floor is more than 80 or 100 below us to not bother? or check for last destination and compare the Y value of the floor.
											MovementManager.StartDescending();
										}
										else
										{
											if (totalLandingStopwatch.ElapsedMilliseconds > 10000)
											{
												Logger.Error("Landing failed. Passing back control.");
												innerMover.MoveStop();
												return;
											}

											if (landingCoroutine == null || landingCoroutine.IsFinished)
											{
												var move = Core.Player.Location.AddRandomDirection2D(10).GetFloor(8);
												MovementManager.StopDescending();
												MovementManager.Jump();
												landingCoroutine = new Coroutine(() => move.MoveToNoMount(false, 0.8f));
												Logger.Info("Created new Landing Unstuck Coroutine, moving to {0}", move);
											}

											if (!landingCoroutine.IsFinished && MovementManager.IsFlying)
											{
												Logger.Verbose("Resumed Landing Unstuck Coroutine");
												while (!landingCoroutine.IsFinished && MovementManager.IsFlying && Behaviors.ShouldContinue
														&& !IsMovingTowardsLocation)
												{
													landingCoroutine.Resume();
													Thread.Sleep(66);
												}
											}

											if (MovementManager.IsFlying)
											{
												landingStopwatch.Restart();
											}
										}

										Thread.Sleep(33);
									}
								}
								finally
								{
									if (IsMovingTowardsLocation)
									{
										Logger.Warn("Landing cancelled after {0} ms. New destination requested.", totalLandingStopwatch.Elapsed);
										innerMover.MoveStop();
									}
									else
									{
										Logger.Info("Landing took {0} ms or less", totalLandingStopwatch.Elapsed);
									}

									totalLandingStopwatch.Reset();
									landingStopwatch.Reset();

									if (Coroutine.Current != landingCoroutine && landingCoroutine != null)
									{
										landingCoroutine.Dispose();
									}

									landingCoroutine = null;

									IsLanding = false;
									landingTask = null;
								}
							});
				}
			}
			else
			{
				IsLanding = false;
			}
		}

		public static explicit operator SlideMover(FlightEnabledSlideMover playerMover)
		{
			return playerMover.innerMover as SlideMover;
		}

		internal static bool ShouldFlyInternal(Vector3 destination)
		{
			return MovementManager.IsFlying
					|| (Actionmanager.CanMount == 0
						&& ((destination.Distance3D(GameObjectManager.LocalPlayer.Location) >= CharacterSettings.Instance.MountDistance)
							|| !destination.IsGround()));
		}

		private void GameEventsOnMapChanged(object sender, EventArgs e)
		{
			ShouldFly = false;
			Logger.Info("Set default value for flying to false until we can determine if we can fly in this zone.");
		}
	}
}