using System;
using System.Linq;
using UnityEngine;

namespace SolarSailNavigator {

    public class SolarSailPart : PartModule {

	// Persistent Variables
	// Sail enabled
	[KSPField(isPersistant = true)]
	public bool IsEnabled = false;
	// Attitude locked
	[KSPField(isPersistant = true)]
	public bool IsLocked = false;
	// Controls
	[KSPField(isPersistant = true)]
	public double UT0;
	[KSPField(isPersistant = true)]
	public string cones;
	[KSPField(isPersistant = true)]
	public string clocks;
	[KSPField(isPersistant = true)]
	public string durations;
	[KSPField(isPersistant = true)]
	public string iwarps;

	// Preview orbit
	public Preview preview;
	protected string previewButtonText = "Show Preview";
	public bool showPreview = false;

	// Sail controls
	public SailControls controls;
	
	// Persistent False
	[KSPField]
	public float reflectedPhotonRatio = 1f;
	[KSPField]
	public float surfaceArea; // Sail surface area
	[KSPField]
	public string animName;
	
	
	// GUI
	// Force and Acceleration
	[KSPField(guiActive = true, guiName = "Force")]
	protected string forceAcquired = "";
	[KSPField(guiActive = true, guiName = "Acceleration")]
	protected string solarAcc = "";
	
	protected Transform surfaceTransform = null;
	protected Animation solarSailAnim = null;
	
	const double kerbin_distance = 13599840256;
	const double thrust_coeff = 9.08e-6;
	
	protected double solar_force_d = 0;
	protected double solar_acc_d = 0;
	protected long count = 0;

	[KSPEvent(guiActive = true, guiName = "Deploy Sail", active = true)]
	public void DeploySail() {
	    if (animName != null && solarSailAnim != null) {
		solarSailAnim[animName].speed = 1f;
		solarSailAnim[animName].normalizedTime = 0f;
		solarSailAnim.Blend(animName, 2f);
	    }
	    IsEnabled = true;
	    // Create control window
	    RenderingManager.AddToPostDrawQueue(3, new Callback(DrawControls));
	}
	
	[KSPEvent(guiActive = true, guiName = "Retract Sail", active = false)]
	public void RetractSail() {
	    if (animName != null && solarSailAnim != null) {
		solarSailAnim[animName].speed = -1f;
		solarSailAnim[animName].normalizedTime = 1f;
		solarSailAnim.Blend(animName, 2f);
	    }
	    IsEnabled = false;
	    // Remove control window
	    RenderingManager.RemoveFromPostDrawQueue(3, new Callback(DrawControls));
	}
	
	// Initialization
	public override void OnStart(StartState state) {

	    if (state != StartState.None && state != StartState.Editor) {

		if (animName != null) {
		    solarSailAnim = part.FindModelAnimators(animName).FirstOrDefault();
		}
		if (IsEnabled) {
		    solarSailAnim[animName].speed = 1f;
		    solarSailAnim[animName].normalizedTime = 0f;
		    solarSailAnim.Blend(animName, 0.1f);
		    RenderingManager.AddToPostDrawQueue(3, new Callback(DrawControls));
		}

		this.part.force_activate();

		// Sail controls
		controls = new SailControls(this);

		// Orbit preview
		preview = new Preview(this);
	    }
	}

	public override void OnUpdate() {
	    // Sail deployment GUI
	    Events["DeploySail"].active = !IsEnabled;
	    Events["RetractSail"].active = IsEnabled;
	    // Text fields (acc & force)
	    Fields["solarAcc"].guiActive = IsEnabled;
	    Fields["forceAcquired"].guiActive = IsEnabled;
	    forceAcquired = solar_force_d.ToString("E") + " N";
	    solarAcc = solar_acc_d.ToString("E") + " m/s";
	}
	
	public override void OnFixedUpdate() {
	    if (FlightGlobals.fetch != null) {
		
		double UT = Planetarium.GetUniversalTime();
		double dT = TimeWarp.fixedDeltaTime;
		
		solar_force_d = 0;
		if (!IsEnabled) { return; }
		
		// Force attitude to sail frame
		if (IsLocked) {
		    SailControl control = controls.Lookup(UT);
		    vessel.SetRotation(SailFrame(vessel.orbit, control.cone, control.clock, UT));
		}

		double sunlightFactor = 1.0;

		if (!inSun(vessel.orbit, UT)) {
		    sunlightFactor = 0.0;
		}

		Vector3d solarForce = CalculateSolarForce(this, vessel.orbit, this.part.transform.up, UT) * sunlightFactor;

		Vector3d solarAccel = solarForce / vessel.GetTotalMass() / 1000.0;

		if (!this.vessel.packed) {
		    vessel.ChangeWorldVelocity(solarAccel * dT);
		} else {
		    PerturbOrbit(vessel.orbit, solarAccel, UT, dT);
		}

		solar_force_d = solarForce.magnitude;
		solar_acc_d = solarAccel.magnitude;
	    }
	    count++;

	    // Update preview orbit if it exists
	    preview.Update(vessel);
	}
		
	private Rect controlWindowPos = new Rect(0, 50, 0, 0);

	private void DrawControls () {
	    if (this.vessel == FlightGlobals.ActiveVessel)
		controlWindowPos = GUILayout.Window(10, controlWindowPos, SailControlsGUI, "Sail Controls");
	}

	private void SailControlsGUI (int WindowID) {

	    GUILayout.BeginVertical();

	    // Lock/Unlock attitude
	    IsLocked = GUILayout.Toggle(IsLocked, "Lock Attitude");

	    // Steering controls
	    controls.GUI();

	    // Preview orbit
	    if (GUILayout.Button(previewButtonText)) {
	    	if (!showPreview) {
		    showPreview = true;
		    preview.Calculate();
		    previewButtonText = "Hide Preview";
		} else {
		    showPreview = false;
		    preview.Destroy();
		    previewButtonText = "Show Preview";
		}
	    }

	    GUILayout.EndVertical();
	    
	    GUI.DragWindow();
	}

	// Calculate RTN frame quaternion given an orbit and UT
	public static Quaternion RTNFrame (Orbit orbit, double UT) {
	    // Position
	    var r = orbit.getRelativePositionAtUT(UT).normalized.xzy;
	    // Velocity
	    var v = orbit.getOrbitalVelocityAtUT(UT).normalized.xzy;
	    // Unit orbit angular momentum
	    var h = Vector3d.Cross(r, v).normalized;
	    // Tangential
	    var t = Vector3d.Cross(h, r).normalized;
	    // QRTN
	    return Quaternion.LookRotation(t, r);
	}

	// Sail frame in local (RTN) coordinates
	public static Quaternion SailFrameLocal (float cone, float clock) {
	    return Quaternion.Euler(0, 90 - clock, cone);
	}
	
	// Sail frame given an orbit, angles, and UT
	public static Quaternion SailFrame (Orbit orbit, float cone, float clock, double UT) {
	    var QRTN = RTNFrame(orbit, UT);
	    var QCC = SailFrameLocal(cone, clock);
	    return QRTN * QCC;
	}

	// Test if an orbit at UT is in sunlight
	public static bool inSun(Orbit orbit, double UT) {
	    Vector3d a = orbit.getPositionAtUT(UT);
	    Vector3d b = FlightGlobals.Bodies[0].getPositionAtUT(UT);
	    foreach (CelestialBody referenceBody in FlightGlobals.Bodies) {
		if (referenceBody.flightGlobalsIndex == 0) { // the sun should not block line of sight to the sun
		    continue;
		}
		Vector3d refminusa = referenceBody.getPositionAtUT(UT) - a;
		Vector3d bminusa = b - a;
		if (Vector3d.Dot(refminusa, bminusa) > 0) {
		    if (Vector3d.Dot(refminusa, bminusa.normalized) < bminusa.magnitude) {
			Vector3d tang = refminusa - Vector3d.Dot(refminusa, bminusa.normalized) * bminusa.normalized;
			if (tang.magnitude < referenceBody.Radius) {
			    return false;
			}
		    }
		}
	    }
	    return true;
	}

	private static double solarForceAtDistance(Vector3d sunPosition, Vector3d ownPosition) {
	    double distance_from_sun = Vector3.Distance(sunPosition, ownPosition);
	    double force_to_return = thrust_coeff * kerbin_distance * kerbin_distance / distance_from_sun / distance_from_sun;
	    return force_to_return;
	}
	
	// Calculate solar force as function of
	// sail, orbit, transform, and UT
	public static Vector3d CalculateSolarForce(SolarSailPart sail, Orbit orbit, Vector3d normal, double UT) {
	    if (sail.part != null) {
		Vector3d sunPosition = FlightGlobals.Bodies[0].getPositionAtUT(UT);
		Vector3d ownPosition = orbit.getPositionAtUT(UT);
		Vector3d ownsunPosition = ownPosition - sunPosition;
		// If normal points away from sun, negate so our force is always away from the sun
		// so that turning the backside towards the sun thrusts correctly
		if (Vector3d.Dot (normal, ownsunPosition) < 0) {
		    normal = -normal;
		}
		// Magnitude of force proportional to cosine-squared of angle between sun-line and normal
		double cosConeAngle = Vector3.Dot (ownsunPosition.normalized, normal);
		
		Vector3d force = normal * cosConeAngle * cosConeAngle * sail.surfaceArea * sail.reflectedPhotonRatio * solarForceAtDistance(sunPosition, ownPosition);
		return force;
	    } else {
		return Vector3d.zero;
	    }
	}

	// Perturn an orbit by an acceleration at UT with time step dT
	public static void PerturbOrbit (Orbit orbit, Vector3d accel, double UT, double dT) {

	    // Return updated orbit if in sun
	    if (accel.magnitude > 0) {
		// Transpose Y and Z for Orbit class
		Vector3d accel_orbit = new Vector3d(accel.x, accel.z, accel.y);
		Vector3d position = orbit.getRelativePositionAtUT(UT);
		Orbit orbit2 = CloneOrbit(orbit);
		orbit2.UpdateFromStateVectors(position, orbit.getOrbitalVelocityAtUT(UT) + accel_orbit * dT, orbit.referenceBody, UT);
		if (!double.IsNaN(orbit2.inclination) && !double.IsNaN(orbit2.eccentricity) && !double.IsNaN(orbit2.semiMajorAxis) && orbit2.timeToAp > dT) {
		    orbit.inclination = orbit2.inclination;
		    orbit.eccentricity = orbit2.eccentricity;
		    orbit.semiMajorAxis = orbit2.semiMajorAxis;
		    orbit.LAN = orbit2.LAN;
		    orbit.argumentOfPeriapsis = orbit2.argumentOfPeriapsis;
		    orbit.meanAnomalyAtEpoch = orbit2.meanAnomalyAtEpoch;
		    orbit.epoch = orbit2.epoch;
		    orbit.referenceBody = orbit2.referenceBody;
		    orbit.Init();
		    orbit.UpdateFromUT(UT);
		}
	    }
	}

	// Dublicate an orbit
	public static Orbit CloneOrbit(Orbit orbit0) {
	    return new Orbit(orbit0.inclination, orbit0.eccentricity, orbit0.semiMajorAxis, orbit0.LAN, orbit0.argumentOfPeriapsis, orbit0.meanAnomalyAtEpoch, orbit0.epoch, orbit0.referenceBody);
	}
    }
}
