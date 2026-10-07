using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild;

public partial class ArenaCamera : Camera3D
{
	[Export] public Node3D? Target;

	private SettingsManager? _settingsManager;
	private float _screenShake = 0.0f;
	private float _screenShakeScale = 1.0f;

	private bool _covenBeamActive = false;
	private float _covenBeamT = 0.0f;

	private const float FollowLerpXZ = 0.06f;
	private const float FollowLerpY = 0.02f;
	private const float FollowHeight = 90.0f;
	private const float FollowForwardOffset = 45.0f;

	private const float GeneralShakeDecay = 0.85f;
	private const float GeneralShakeCutoff = 0.02f;

	private const float CovenPeakStrength = 0.9f;
	private const float CovenFadeInSeconds = 0.5f;
	private const float CovenDuration = 10.0f;
	private const float CovenFadeOutStart = 0.85f;
	private const float CovenFadeOutWindow = 0.15f;

	public override void _Ready()
	{
		_settingsManager = SettingsManager.Resolve(this);
		if (_settingsManager != null)
		{
			_settingsManager.GraphicsSettingsChanged += OnGraphicsSettingsChanged;
			OnGraphicsSettingsChanged(_settingsManager.GraphicsSettings);
		}
	}

	public override void _ExitTree()
	{
		if (_settingsManager != null)
		{
			_settingsManager.GraphicsSettingsChanged -= OnGraphicsSettingsChanged;
		}
	}

	public override void _Process(double delta)
	{
		if (Target == null || !GodotObject.IsInstanceValid(Target))
		{
			return;
		}

		Vector3 playerPos = Target.GlobalPosition;
		Vector3 camPos = GlobalPosition;

		camPos.X += (playerPos.X - camPos.X) * FollowLerpXZ;
		camPos.Z += ((playerPos.Z + FollowForwardOffset) - camPos.Z) * FollowLerpXZ;
		camPos.Y += (FollowHeight - camPos.Y) * FollowLerpY;

		if (_covenBeamActive)
		{
			float fadeIn = Mathf.Min(_covenBeamT / CovenFadeInSeconds, 1.0f);
			float progress = _covenBeamT / CovenDuration;
			float fadeOut = progress > CovenFadeOutStart
				? 1.0f - ((progress - CovenFadeOutStart) / CovenFadeOutWindow)
				: 1.0f;

			float shakeStr = CovenPeakStrength * fadeIn * fadeOut * _screenShakeScale;

			camPos.X += (GD.Randf() - 0.5f) * shakeStr;
			camPos.Z += (GD.Randf() - 0.5f) * shakeStr;
			camPos.Y += (GD.Randf() - 0.5f) * shakeStr * 0.3f;
		}

		if (_screenShake > 0.0f)
		{
			float shakeAmount = _screenShake * _screenShakeScale;
			camPos.X += (GD.Randf() - 0.5f) * shakeAmount;
			camPos.Z += (GD.Randf() - 0.5f) * shakeAmount;
			camPos.Y += (GD.Randf() - 0.5f) * shakeAmount * 0.3f;

			_screenShake *= GeneralShakeDecay;
			if (_screenShake < GeneralShakeCutoff)
			{
				_screenShake = 0.0f;
			}
		}

		GlobalPosition = camPos;
		LookAt(new Vector3(playerPos.X, 0.0f, playerPos.Z), Vector3.Up);
	}

	public void AddScreenShake(float amount)
	{
		_screenShake = Mathf.Max(_screenShake, amount);
	}

	private void OnGraphicsSettingsChanged(GraphicsSettingsData settings)
	{
		_screenShakeScale = settings.ScreenShakeScale;
	}

	public void SetCovenBeam(bool active, float beamTime)
	{
		_covenBeamActive = active;
		_covenBeamT = beamTime;
	}
}
