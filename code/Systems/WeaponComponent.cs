using Sandbox;
using Sandbox.Citizen;

namespace CafeGame;

public abstract class WeaponComponent : Component
{
	[Property] public string DisplayName { get; set; }
	[Property] public float ReloadTime { get; set; } = 2f;
	[Property] public float DeployTime { get; set; } = 0.5f;
	[Property] public float FireRate { get; set; } = 3f;
	[Property] public float Spread { get; set; } = 0.01f;
	[Property] public float Damage { get; set; } = 10f;
	[Property] public int ClipSize { get; set; } = 30;
	[Property] public CitizenAnimationHelper.HoldTypes HoldType { get; set; } = CitizenAnimationHelper.HoldTypes.Pistol;
	[Property] public SoundEvent DeploySound { get; set; }
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundSequenceData ReloadSoundSequence { get; set; }
	[Property] public bool IsDeployed { get; set; }
	
	[Sync] public bool IsReloading { get; set; }
	[Sync] public int AmmoInClip { get; set; }
	
	private SkinnedModelRenderer ModelRenderer { get; set; }
	private SoundSequence ReloadSound { get; set; }
	private TimeUntil ReloadFinishTime { get; set; }
	private TimeUntil NextAttackTime { get; set; }

	[Broadcast]
	public void Deploy()
	{
		if ( !IsDeployed )
		{
			IsDeployed = true;
			OnDeployed();
		}
	}

	[Broadcast]
	public void Holster()
	{
		if ( IsDeployed )
		{
			OnHolstered();
			IsDeployed = false;
		}
	}
	
	public virtual bool DoPrimaryAttack()
	{
		if ( !NextAttackTime ) return false;
		if ( IsReloading ) return false;

		var player = Components.GetInAncestors<Player>();
		
		var renderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		var attachment = renderer.GetAttachment( "muzzle" );
		var startPos = Scene.Camera.Transform.Position;
		var direction = Scene.Camera.Transform.Rotation.Forward;
		direction += Vector3.Random * Spread;
		
		var endPos = startPos + direction * 10000f;
		var trace = Scene.Trace.Ray( startPos, endPos )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.UsePhysicsWorld()
			.UseHitboxes()
			.Run();

		var damage = Damage;
		var origin = attachment?.Position ?? startPos;

		SendAttackMessage( origin, trace.EndPosition, trace.Distance );

		NextAttackTime = 1f / FireRate;
		AmmoInClip--;

		return true;
	}

	public virtual bool DoReload()
	{
		var ammoToTake = ClipSize - AmmoInClip;
		if ( ammoToTake <= 0 )
			return false;
		
		var player = Components.GetInAncestors<Player>();
		if ( !player.IsValid() )
			return false;

		ReloadFinishTime = ReloadTime;
		IsReloading = true;
			
		SendReloadMessage();
			
		return true;
	}

	protected override void OnStart()
	{
		if ( IsDeployed )
			OnDeployed();
		else
			OnHolstered();
		
		base.OnStart();
	}

	protected virtual void OnDeployed()
	{
		var player = Components.GetInAncestors<Player>();

		if ( player.IsValid() )
		{
			foreach ( var animator in player.Animators )
			{
				animator.TriggerDeploy();
			}
		}
		
		if ( ModelRenderer.IsValid() )
		{
			ModelRenderer.Enabled = true;
		}
		
		if ( DeploySound is not null )
		{
			Sound.Play( DeploySound, Transform.Position );
		}
		
		NextAttackTime = DeployTime;
	}

	protected virtual void OnHolstered()
	{
		if ( ModelRenderer.IsValid() )
		{
			ModelRenderer.Enabled = false;
		}

		ReloadSound?.Stop();
	}

	protected override void OnAwake()
	{
		ModelRenderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>( true );
		base.OnAwake();
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy && ReloadFinishTime )
		{
			IsReloading = false;
		}

		ReloadSound?.Update( Transform.Position );

		base.OnUpdate();
	}

	protected override void OnDestroy()
	{
		if ( IsDeployed )
		{
			OnHolstered();
			IsDeployed = false;
		}
		
		base.OnDestroy();
	}

	[Broadcast]
	private void SendReloadMessage()
	{
		if ( ReloadSoundSequence is null )
			return;
		
		ReloadSound?.Stop();
		
		ReloadSound = new( ReloadSoundSequence );
		ReloadSound.Start( Transform.Position );
	}
	
	[Broadcast]
	private void SendAttackMessage( Vector3 startPos, Vector3 endPos, float distance )
	{
		if ( FireSound is not null )
		{
			Sound.Play( FireSound, startPos );
		}
	}
}
