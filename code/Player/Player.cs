using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.Citizen;

namespace CafeGame;

[Group( "Arena" )]
[Title( "Player Controller" )]
public class Player : Component, Component.ITriggerListener
{
	public static Player Local => GameManager.ActiveScene.GetAllComponents<Player>().FirstOrDefault( p => p.Network.IsOwner );

	[Property] public Vector3 Gravity { get; set; } = new ( 0f, 0f, 800f );
	
	public CharacterController CharacterController { get; private set; }
	public SkinnedModelRenderer ModelRenderer { get; private set; }
	public List<CitizenAnimationHelper> Animators { get; private set; } = new();
	public Vector3 WishVelocity { get; private set; }
	
	[Property] private CitizenAnimationHelper ShadowAnimator { get; set; }
	[Property] public Inventory Weapons { get; set; }
	[Property] public GameObject Head { get; set; }
	[Property] public GameObject Eye { get; set; }
	[Property] public CitizenAnimationHelper AnimationHelper { get; set; }
	[Property] public bool SicknessMode { get; set; }
	[Property] public bool EnableCrouching { get; set; }
	[Property] public float StandHeight { get; set; } = 64f;
	[Property] public float DuckHeight { get; set; } = 28f;

	[Sync] public Angles EyeAngles { get; set; }
	[Sync] public bool IsRunning { get; set; }
	[Sync] public bool IsCrouching { get; set; }

	private RealTimeSince LastGroundedTime { get; set; }
	private RealTimeSince LastUngroundedTime { get; set; }
	private bool WantsToCrouch { get; set; }

	public void ResetViewAngles()
	{
		var rotation = Rotation.Identity;
		EyeAngles = rotation.Angles().WithRoll( 0f );
	}

	public async void RespawnAsync( float seconds )
	{
		if ( IsProxy ) return;

		await Task.DelaySeconds( seconds );
		Respawn();
	}

	public void Respawn()
	{
		if ( IsProxy )
			return;

		Weapons.Clear();
		Weapons.GiveDefault();
		
		MoveToSpawnPoint();
	}

	protected virtual bool CanUncrouch()
	{
		if ( !IsCrouching ) return true;
		if ( LastUngroundedTime < 0.2f ) return false;

		var cc = GameObject.Components.Get<CharacterController>();
		var tr = cc.TraceDirection( Vector3.Up * DuckHeight );
		return !tr.Hit;
	}

	protected override void OnAwake()
	{
		base.OnAwake();
		
		ModelRenderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		
		CharacterController = Components.GetInDescendantsOrSelf<CharacterController>();
		CharacterController.IgnoreLayers.Add( "player" );
		
		if ( CharacterController.IsValid() )
		{
			CharacterController.Height = StandHeight;
		}
		
		if ( IsProxy )
			return;

		ResetViewAngles();
	}

	protected override void OnStart()
	{
		Animators.Add( ShadowAnimator );
		Animators.Add( AnimationHelper );

		if ( !IsProxy )
		{
			Respawn();
		}
			
		base.OnStart();
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();

		if ( !Scene.IsValid() || !Scene.Camera.IsValid() )
			return;
		
		if ( ModelRenderer.IsValid() )
		{
			ModelRenderer.SetBodyGroup( "head", IsProxy ? 0 : 1 );

			var shadowRenderer = ShadowAnimator.Components.Get<SkinnedModelRenderer>( true );
			
			ModelRenderer.RenderType = IsProxy
				? Sandbox.ModelRenderer.ShadowRenderType.On
				: Sandbox.ModelRenderer.ShadowRenderType.Off;

			shadowRenderer.Enabled = true;

			var clothing = ModelRenderer.Components.GetAll<ClothingComponent>()
				.Where( c => c.Category is Clothing.ClothingCategory.Hair
					or Clothing.ClothingCategory.Facial
					or Clothing.ClothingCategory.Hat );

			foreach ( var c in clothing )
			{
				c.ModelRenderer.RenderType = IsProxy ? Sandbox.ModelRenderer.ShadowRenderType.On : Sandbox.ModelRenderer.ShadowRenderType.ShadowsOnly;
			}
		}
		
		if ( IsProxy )
			return;

		if ( !Eye.IsValid() )
			return;
		
		var idealEyePos = Eye.Transform.Position;
		var headPosition = Transform.Position + Vector3.Up * CharacterController.Height;
		var headTrace = Scene.Trace.Ray( Transform.Position, headPosition )
			.UsePhysicsWorld()
			.IgnoreGameObjectHierarchy( GameObject )
			.WithAnyTags( "solid" )
			.Run();

		headPosition = headTrace.EndPosition - headTrace.Direction * 2f;
	
		var trace = Scene.Trace.Ray( headPosition, idealEyePos )
			.UsePhysicsWorld()
			.IgnoreGameObjectHierarchy( GameObject )
			.WithAnyTags( "solid" )
			.Radius( 2f )
			.Run();
	
		Scene.Camera.Transform.Position = trace.Hit ? trace.EndPosition : idealEyePos;
		
		if ( SicknessMode )
			Scene.Camera.Transform.Rotation = Rotation.LookAt( Eye.Transform.Rotation.Left ) * Rotation.FromPitch( -10f );
		else
			Scene.Camera.Transform.Rotation = EyeAngles.ToRotation() * Rotation.FromPitch( -10f );
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy )
		{
			var angles = EyeAngles.Normal;
			angles += Input.AnalogLook * 0.5f;
			angles.pitch = angles.pitch.Clamp( -60f, 80f );
			
			EyeAngles = angles.WithRoll( 0f );
			IsRunning = Input.Down( "Run" );
		}
		
		var weapon = Weapons.Deployed;

		foreach ( var animator in Animators )
		{
			animator.HoldType = weapon.IsValid() ? weapon.HoldType : CitizenAnimationHelper.HoldTypes.None;
			animator.WithVelocity( CharacterController.Velocity );
			animator.WithWishVelocity( WishVelocity );
			animator.IsGrounded = CharacterController.IsOnGround;
			animator.FootShuffle = 0f;
			animator.DuckLevel = IsCrouching ? 1f : 0f;
			animator.WithLook( EyeAngles.Forward );
			animator.MoveStyle = ( IsRunning && !IsCrouching ) ? CitizenAnimationHelper.MoveStyles.Run : CitizenAnimationHelper.MoveStyles.Walk;
		}
	}

	protected virtual void DoCrouchingInput()
	{
		WantsToCrouch = EnableCrouching && CharacterController.IsOnGround && Input.Down( "Duck" );

		if ( WantsToCrouch == IsCrouching )
			return;
		
		if ( WantsToCrouch )
		{
			CharacterController.Height = DuckHeight;
			IsCrouching = true;
		}
		else
		{
			if ( !CanUncrouch() )
				return;

			CharacterController.Height = StandHeight;
			IsCrouching = false;
		}
	}

	protected virtual void DoMovementInput()
	{
		BuildWishVelocity();

		if ( CharacterController.IsOnGround && Input.Down( "Jump" ) )
		{
			CharacterController.Punch( Vector3.Up * 300f );
			SendJumpMessage();
		}

		if ( CharacterController.IsOnGround )
		{
			CharacterController.Velocity = CharacterController.Velocity.WithZ( 0f );
			CharacterController.Accelerate( WishVelocity );
			CharacterController.ApplyFriction( 4.0f );
		}
		else
		{
			CharacterController.Velocity -= Gravity * Time.Delta * 0.5f;
			CharacterController.Accelerate( WishVelocity.ClampLength( 50f ) );
			CharacterController.ApplyFriction( 0.1f );
		}
		
		CharacterController.Move();

		if ( !CharacterController.IsOnGround )
		{
			CharacterController.Velocity -= Gravity * Time.Delta * 0.5f;
			LastUngroundedTime = 0f;
		}
		else
		{
			CharacterController.Velocity = CharacterController.Velocity.WithZ( 0 );
			LastGroundedTime = 0f;
		}

		Transform.Rotation = Rotation.FromYaw( EyeAngles.ToRotation().Yaw() );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		DoCrouchingInput();
		DoMovementInput();

		if ( Input.MouseWheel.y > 0 )
			Weapons.Next();
		else if ( Input.MouseWheel.y < 0 )
			Weapons.Previous();

		var weapon = Weapons.Deployed;
		if ( !weapon.IsValid() ) return;

		if ( Input.Down( "Attack1" ) )
		{
			if ( weapon.DoPrimaryAttack() )
			{
				SendAttackMessage();
			}
		}
	}
	
	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		/*var pickup = other.Components.GetInAncestorsOrSelf<PickupComponent>();

		if ( pickup.IsValid() )
		{
			pickup.Pickup( GameObject );
		}*/
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		
	}
	
	private void MoveToSpawnPoint()
	{
		if ( IsProxy )
			return;
		
		var spawnpoints = Scene.GetAllComponents<SpawnPoint>();
		var randomSpawnpoint = Game.Random.FromList( spawnpoints.ToList() );

		Transform.Position = randomSpawnpoint.Transform.Position;
		Transform.Rotation = Rotation.FromYaw( randomSpawnpoint.Transform.Rotation.Yaw() );
		EyeAngles = Transform.Rotation;
	}

	private void BuildWishVelocity()
	{
		var rotation = EyeAngles.ToRotation();

		WishVelocity = rotation * Input.AnalogMove;
		WishVelocity = WishVelocity.WithZ( 0f );

		if ( !WishVelocity.IsNearZeroLength )
			WishVelocity = WishVelocity.Normal;

		if ( IsCrouching )
			WishVelocity *= 64f;
		else if ( IsRunning )
			WishVelocity *= 260f;
		else
			WishVelocity *= 110f;
	}
	
	
	[Broadcast]
	private void SendReloadMessage()
	{
		foreach ( var animator in Animators )
		{
			var renderer = animator.Components.Get<SkinnedModelRenderer>( FindMode.EnabledInSelfAndDescendants );
			renderer?.Set( "b_reload", true );
		}
	}

	[Broadcast]
	private void SendAttackMessage()
	{
		foreach ( var animator in Animators )
		{
			var renderer = animator.Components.Get<SkinnedModelRenderer>( FindMode.EnabledInSelfAndDescendants );
			renderer?.Set( "b_attack", true );
		}
	}
	
	[Broadcast]
	private void SendJumpMessage()
	{
		foreach ( var animator in Animators )
		{
			animator.TriggerJump();
		}
	}
}
