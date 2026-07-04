using UnityEngine;

namespace CGJ2026.View
{
    /// <summary>
    /// Dependency-free analytic two-bone IK for one 2D leg. The foot alternates between a
    /// world-space planted state and a short swing state, so moving the character or playing an
    /// upper-body animation does not make the foot skate over the ground.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class LegIK2D : MonoBehaviour
    {
        public enum FootPlantState
        {
            Airborne,
            Planted,
            Stepping
        }

        [Header("Segments")]
        [Tooltip("White-box thigh. Its local down axis must point from hip to knee.")]
        [SerializeField] private Transform thigh;
        [Tooltip("White-box shin. Its local down axis must point from knee to ankle.")]
        [SerializeField] private Transform shin;
        [Tooltip("Optional foot/boot transform. Its local right axis is aligned to the slope.")]
        [SerializeField] private Transform foot;
        [Min(0.01f)] [SerializeField] private float thighLength = 0.6f;
        [Min(0.01f)] [SerializeField] private float shinLength = 0.6f;

        [Header("Animator Bones")]
        [Tooltip("Optional humanoid Animator source. White-box rigs can leave this empty and use the explicit transforms above.")]
        [SerializeField] private Animator boneAnimator;
        [SerializeField] private bool useAnimatorBones;
        [SerializeField] private HumanBodyBones upperLegBone = HumanBodyBones.LeftUpperLeg;
        [SerializeField] private HumanBodyBones lowerLegBone = HumanBodyBones.LeftLowerLeg;
        [SerializeField] private HumanBodyBones footBone = HumanBodyBones.LeftFoot;

        [Header("Stance")]
        [Tooltip("Desired ankle position in hip-local space. Give the two legs opposite X offsets.")]
        [SerializeField] private Vector2 restFootOffset = new Vector2(0f, -1.1f);
        [Tooltip("Minimum horizontal distance from the body centerline. Prevents idle feet from crossing.")]
        [Min(0f)] [SerializeField] private float minFootSideOffset = 0.12f;
        [Tooltip("Idle stance rules (side separation, crossing release) only apply below this body speed. A moving gait must let the feet cross the centerline.")]
        [Min(0f)] [SerializeField] private float maxStanceRuleSpeed = 0.3f;
        [Tooltip("Use 1 or -1 to choose which side the knee bends toward.")]
        [SerializeField] private float kneeBendSign = 1f;
        [Tooltip("The other leg prevents both feet from starting a stride on the same frame.")]
        [SerializeField] private LegIK2D partnerLeg;
        [Tooltip("Optional source for movement prediction. Normally the player Rigidbody2D.")]
        [SerializeField] private Rigidbody2D motionBody;

        [Header("Ground Probe")]
        [SerializeField] private LayerMask groundMask;
        [Min(0f)] [SerializeField] private float probeUp = 0.6f;
        [Min(0.01f)] [SerializeField] private float probeExtra = 0.65f;
        [Range(0f, 89f)] [SerializeField] private float maxGroundAngle = 70f;
        [Tooltip("Distance from the foot pivot to its sole, measured along the ground normal.")]
        [Min(0f)] [SerializeField] private float footSurfaceOffset = 0.08f;
        [Tooltip("Hip-driven downward probe length adds the rest-foot drop to this extra distance.")]
        [Min(0.01f)] [SerializeField] private float hipProbeExtra = 1.45f;
        [Tooltip("How far in front of the estimated knee the knee probe is allowed to look.")]
        [Min(0f)] [SerializeField] private float kneeProbeForward = 0f;
        [Tooltip("Extra reach beyond the shin length before the knee probe clamps the foot target.")]
        [Min(0f)] [SerializeField] private float kneeReachSlack = 0.24f;

        [Header("Gait")]
        [Tooltip("How far the planted foot may drift from its desired point before taking a step.")]
        [Min(0.01f)] [SerializeField] private float strideDistance = 0.34f;
        [Tooltip("Body travel from the last planted frame that starts a release/step cycle.")]
        [Min(0.01f)] [SerializeField] private float bodyStepDistance = 0.28f;
        [Tooltip("How quickly an active step may chase the moving body's next foot target.")]
        [Min(0.01f)] [SerializeField] private float stepTargetFollowSpeed = 18f;
        [Tooltip("Forward landing offset used only by the released/stepping leg.")]
        [Min(0f)] [SerializeField] private float activeStepForwardDistance = 0.42f;
        [Tooltip("Extra forward landing lead from movement speed, clamped by maxActiveStepSpeedLead.")]
        [Min(0f)] [SerializeField] private float activeStepSpeedLeadTime = 0.035f;
        [Min(0f)] [SerializeField] private float maxActiveStepSpeedLead = 0.28f;
        [Tooltip("Adds a little duration to longer emergency steps so fast movement does not look like a pop.")]
        [Min(0f)] [SerializeField] private float extraStepDurationPerUnit = 0.08f;
        [Tooltip("When the desired foot direction diverges from player movement by this angle, release the foot.")]
        [Range(0f, 180f)] [SerializeField] private float movementAngleStepThreshold = 52f;
        [Tooltip("When the foot tangent would twist this far away from movement, release instead of dragging the ankle.")]
        [Range(0f, 180f)] [SerializeField] private float ankleTwistStepThreshold = 58f;
        [Min(0.01f)] [SerializeField] private float strideDuration = 0.16f;
        [Tooltip("Speeds up step cadence as the body moves faster: stride duration is divided by (1 + speed * factor).")]
        [Min(0f)] [SerializeField] private float cadenceSpeedFactor = 0.5f;
        [Tooltip("Lower bound for the sped-up stride duration so steps never finish instantly.")]
        [Min(0.01f)] [SerializeField] private float minStrideDuration = 0.05f;
        [Min(0f)] [SerializeField] private float stepHeight = 0.18f;
        [Min(0f)] [SerializeField] private float velocityLeadTime = 0.06f;
        [Min(0f)] [SerializeField] private float minMoveSpeedForDirection = 0.08f;
        [Tooltip("Large discontinuities are treated as teleports and snap the plant safely.")]
        [Min(0.1f)] [SerializeField] private float teleportDistance = 1.75f;
        [Min(0.001f)] [SerializeField] private float airborneSmoothTime = 0.06f;
        [Tooltip("How quickly the sampled sole normal blends into the solved foot normal.")]
        [Min(0f)] [SerializeField] private float normalSmoothTime = 0.04f;
        [Tooltip("How quickly the planted/released IK weight changes. Higher is snappier.")]
        [Min(0.01f)] [SerializeField] private float plantBlendSpeed = 12f;
        [Tooltip("Caps IK simulation time per frame so high speed or hitchy frames cannot finish a whole step instantly.")]
        [Min(0.001f)] [SerializeField] private float maxIkDeltaTime = 0.033f;
        [Tooltip("Maximum visual foot correction speed in world units per second.")]
        [Min(0.01f)] [SerializeField] private float maxFootCorrectionSpeed = 10f;
        [Min(1f)] [SerializeField] private float footTurnSpeed = 720f;
        [Min(1f)] [SerializeField] private float plantedFootTurnSpeed = 240f;
        [Min(1f)] [SerializeField] private float releasedFootTurnSpeed = 540f;

        public Vector2 FootWorldPosition { get; private set; }
        public Vector2 GroundNormal { get; private set; } = Vector2.up;
        public Vector2 KneeProbePosition { get; private set; }
        public Collider2D SupportCollider { get; private set; }
        public FootPlantState PlantState { get; private set; } = FootPlantState.Airborne;
        public bool IsFootGrounded => PlantState == FootPlantState.Planted;
        public bool IsStepping => PlantState == FootPlantState.Stepping;
        public float FootPlantWeight { get; private set; }
        public float MaxReach => Mathf.Max(0.01f, thighLength + shinLength - ReachEpsilon);

        private const float ReachEpsilon = 0.005f;
        // An alternating gait plants each foot for roughly two swing phases, so a landing must
        // lead the body by at least 2 * speed * swingDuration (plus margin) to avoid falling behind.
        private const float GaitSyncFactor = 2.4f;

        private bool initialized;
        private Vector2 currentTarget;
        private Vector2 currentNormal = Vector2.up;
        private Vector2 plantedTarget;
        private Vector2 plantedNormal = Vector2.up;
        private Vector2 stepFrom;
        private Vector2 stepTo;
        private Vector2 stepNormalFrom = Vector2.up;
        private Vector2 stepNormalTo = Vector2.up;
        private Vector2 airborneVelocity;
        private float stepElapsed;
        private float activeStepDuration;
        private float currentFootAngle;
        private bool hasSmoothedFootPosition;
        private Vector2 smoothedFootPosition;
        private Vector2 lastHipPosition;
        private Vector2 lastBodyPosition;
        private Vector2 plantedBodyPosition;
        private Vector2 travelDirection = Vector2.right;
        private float stanceSideSign = 1f;
        private int lastStepStartFrame = -1;

        private void Reset()
        {
            motionBody = GetComponentInParent<Rigidbody2D>();
        }

        private void Awake()
        {
            ResolveAnimatorBones();
            ResolveStanceSide();

            if (motionBody == null)
            {
                motionBody = GetComponentInParent<Rigidbody2D>();
            }

            currentFootAngle = foot != null ? SignedAngle(foot.eulerAngles.z) : 0f;
        }

        private void OnEnable()
        {
            initialized = false;
            PlantState = FootPlantState.Airborne;
            FootPlantWeight = 0f;
            hasSmoothedFootPosition = false;
            airborneVelocity = Vector2.zero;
        }

        private void OnValidate()
        {
            thighLength = Mathf.Max(0.01f, thighLength);
            shinLength = Mathf.Max(0.01f, shinLength);
            kneeBendSign = kneeBendSign < 0f ? -1f : 1f;
            strideDuration = Mathf.Max(0.01f, strideDuration);
            maxStanceRuleSpeed = Mathf.Max(0f, maxStanceRuleSpeed);
            cadenceSpeedFactor = Mathf.Max(0f, cadenceSpeedFactor);
            minStrideDuration = Mathf.Max(0.01f, minStrideDuration);
            bodyStepDistance = Mathf.Max(0.01f, bodyStepDistance);
            stepTargetFollowSpeed = Mathf.Max(0.01f, stepTargetFollowSpeed);
            activeStepForwardDistance = Mathf.Max(0f, activeStepForwardDistance);
            activeStepSpeedLeadTime = Mathf.Max(0f, activeStepSpeedLeadTime);
            maxActiveStepSpeedLead = Mathf.Max(0f, maxActiveStepSpeedLead);
            extraStepDurationPerUnit = Mathf.Max(0f, extraStepDurationPerUnit);
            airborneSmoothTime = Mathf.Max(0.001f, airborneSmoothTime);
            normalSmoothTime = Mathf.Max(0f, normalSmoothTime);
            plantBlendSpeed = Mathf.Max(0.01f, plantBlendSpeed);
            maxIkDeltaTime = Mathf.Max(0.001f, maxIkDeltaTime);
            maxFootCorrectionSpeed = Mathf.Max(0.01f, maxFootCorrectionSpeed);
            footTurnSpeed = Mathf.Max(1f, footTurnSpeed);
            plantedFootTurnSpeed = Mathf.Max(1f, plantedFootTurnSpeed);
            releasedFootTurnSpeed = Mathf.Max(1f, releasedFootTurnSpeed);
        }

        private void LateUpdate()
        {
            Vector2 hip = GetHipPosition();
            Vector2 bodyPosition = GetBodyPosition(hip);
            UpdateTravelDirection(bodyPosition);

            Vector2 restWorld = GetRestWorldPosition(hip);
            bool foundGround = TryGetGroundTarget(hip, bodyPosition, restWorld, out Vector2 desiredTarget, out Vector2 desiredNormal);
            bool teleported = initialized && IsActualTeleport(hip);

            if (!initialized)
            {
                InitializeTarget(hip, bodyPosition, foundGround, desiredTarget, desiredNormal, restWorld);
            }
            else if (teleported)
            {
                ResetAfterTeleport(hip, bodyPosition, foundGround, desiredTarget, desiredNormal, restWorld);
            }
            else if (foundGround)
            {
                UpdateGroundedTarget(hip, bodyPosition, desiredTarget, desiredNormal);
            }
            else
            {
                UpdateAirborneTarget(hip, restWorld);
            }

            Vector2 reachableTarget = ClampToReach(hip, currentTarget);
            // If the clamp had to move a planted target the sole is no longer on the ground,
            // so consumers of FootPlantWeight must not treat this foot as supported.
            bool plantPulledOffGround = IsFootGrounded
                && (reachableTarget - currentTarget).sqrMagnitude > 0.0004f;
            UpdatePlantWeight(plantPulledOffGround);
            Vector2 solvedTarget = SmoothSolvedFootTarget(reachableTarget, teleported);
            FootWorldPosition = solvedTarget;
            GroundNormal = currentNormal;
            Solve(hip, solvedTarget, currentNormal);
            lastHipPosition = hip;
            lastBodyPosition = bodyPosition;
        }

        private Vector2 GetRestWorldPosition(Vector2 hip)
        {
            Vector2 rest = hip + (Vector2)transform.TransformVector(restFootOffset);
            if (motionBody == null || velocityLeadTime <= 0f)
            {
                return rest;
            }

            float maxLead = strideDistance * 0.75f;
            float forwardSpeed = Vector2.Dot(motionBody.linearVelocity, travelDirection);
            float lead = Mathf.Clamp(forwardSpeed * velocityLeadTime, -maxLead, maxLead);
            rest += travelDirection * lead;
            return rest;
        }

        private bool TryGetGroundTarget(Vector2 hip, Vector2 bodyPosition, Vector2 restWorld, out Vector2 target, out Vector2 normal)
        {
            Vector2 hipOrigin = hip + Vector2.up * probeUp;
            float hipDistance = probeUp + Mathf.Abs(restFootOffset.y) + GetEffectiveHipProbeExtra();
            RaycastHit2D hipHit = Physics2D.Raycast(hipOrigin, Vector2.down, hipDistance, groundMask);

            Vector2 restOrigin = restWorld + Vector2.up * probeUp;
            float restDistance = probeUp + probeExtra;
            RaycastHit2D restHit = Physics2D.Raycast(restOrigin, Vector2.down, restDistance, groundMask);
            RaycastHit2D hit = IsValidGroundHit(restHit) ? restHit : hipHit;
            float minimumUpDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);

            if (hit.collider == null || Vector2.Dot(hit.normal, Vector2.up) < minimumUpDot)
            {
                target = restWorld;
                normal = Vector2.up;
                SupportCollider = null;
                return false;
            }

            normal = NormalizeOrUp(hit.normal);
            target = hit.point + normal * footSurfaceOffset;
            SupportCollider = hit.collider;
            ApplyKneeProbeConstraint(hip, ref target, ref normal);
            if (IsNearlyStationary())
            {
                ConstrainTargetToBodySide(bodyPosition, ref target, ref normal);
            }

            return true;
        }

        private void InitializeTarget(
            Vector2 hip,
            Vector2 bodyPosition,
            bool foundGround,
            Vector2 desiredTarget,
            Vector2 desiredNormal,
            Vector2 restWorld)
        {
            initialized = true;
            PlantState = foundGround ? FootPlantState.Planted : FootPlantState.Airborne;
            currentTarget = foundGround ? desiredTarget : ClampToReach(hip, restWorld);
            currentNormal = foundGround ? NormalizeOrUp(desiredNormal) : Vector2.up;
            plantedTarget = currentTarget;
            plantedNormal = currentNormal;
            lastHipPosition = hip;
            lastBodyPosition = bodyPosition;
            plantedBodyPosition = bodyPosition;
            FootPlantWeight = foundGround ? 1f : 0f;
        }

        private void UpdateGroundedTarget(Vector2 hip, Vector2 bodyPosition, Vector2 desiredTarget, Vector2 desiredNormal)
        {
            if (IsStepping)
            {
                RetargetActiveStep(bodyPosition, desiredTarget, desiredNormal);
                AdvanceStep();
                return;
            }

            PlantState = FootPlantState.Planted;
            float desiredDrift = Vector2.Distance(plantedTarget, desiredTarget);
            float hipStretch = Vector2.Distance(hip, plantedTarget);
            float bodyTravel = Mathf.Abs(Vector2.Dot(bodyPosition - plantedBodyPosition, travelDirection));
            float footTravelAngle = GetFootTravelAngle(plantedTarget, desiredTarget);
            float ankleTwistAngle = GetAnkleTwistAngle(desiredNormal);
            bool crossedCenterline = IsNearlyStationary() && IsFootAcrossBody(plantedTarget, bodyPosition);
            // Stretch/travel/angle releases only apply to the trailing foot. A foot planted ahead
            // of the body always sees the desired target behind it, so ungated these triggers
            // re-release every fresh landing and the gait degenerates into vibration.
            bool footTrailing = Vector2.Dot(plantedTarget - bodyPosition, travelDirection) <= 0f;

            bool overStride = desiredDrift >= strideDistance
                || crossedCenterline
                || ankleTwistAngle >= ankleTwistStepThreshold
                || (footTrailing
                    && (hipStretch >= MaxReach * 0.94f
                        || bodyTravel >= bodyStepDistance
                        || footTravelAngle >= movementAngleStepThreshold));
            // Past full reach the clamp would drag this "planted" foot off the ground, so release
            // even while the partner is mid-swing; a brief two-foot flight reads better than hovering.
            bool emergencyRelease = hipStretch >= MaxReach;
            if ((overStride && CanStartStep()) || emergencyRelease)
            {
                StartStep(bodyPosition, desiredTarget, desiredNormal);
                AdvanceStep();
                return;
            }

            currentTarget = plantedTarget;
            currentNormal = SmoothNormal(currentNormal, plantedNormal);
        }

        private bool CanStartStep()
        {
            return partnerLeg == null
                || !partnerLeg.isActiveAndEnabled
                || (!partnerLeg.IsStepping && partnerLeg.lastStepStartFrame != Time.frameCount);
        }

        private void StartStep(Vector2 bodyPosition, Vector2 desiredTarget, Vector2 desiredNormal)
        {
            PlantState = FootPlantState.Stepping;
            stepElapsed = 0f;
            stepFrom = currentTarget;
            stepTo = BuildForwardStepTarget(bodyPosition, desiredTarget, ref desiredNormal);
            stepNormalFrom = NormalizeOrUp(currentNormal);
            stepNormalTo = NormalizeOrUp(desiredNormal);
            float extraDistance = Mathf.Max(0f, Vector2.Distance(stepFrom, stepTo) - strideDistance);
            float cadenceScale = GetScaledStrideDuration(GetForwardSpeed()) / strideDuration;
            activeStepDuration = Mathf.Max(
                minStrideDuration,
                (strideDuration + extraDistance * extraStepDurationPerUnit) * cadenceScale);
            airborneVelocity = Vector2.zero;
            lastStepStartFrame = Time.frameCount;
        }

        private void RetargetActiveStep(Vector2 bodyPosition, Vector2 desiredTarget, Vector2 desiredNormal)
        {
            Vector2 forwardTarget = BuildForwardStepTarget(bodyPosition, desiredTarget, ref desiredNormal);
            float deltaTime = GetIkDeltaTime();
            float blend = deltaTime > 0f
                ? 1f - Mathf.Exp(-stepTargetFollowSpeed * deltaTime)
                : 1f;
            stepTo = Vector2.Lerp(stepTo, forwardTarget, blend);
            stepNormalTo = NormalizeOrUp(Vector2.Lerp(stepNormalTo, desiredNormal, blend));
        }

        private Vector2 BuildForwardStepTarget(Vector2 bodyPosition, Vector2 desiredTarget, ref Vector2 desiredNormal)
        {
            float forwardSpeed = GetForwardSpeed();
            if (forwardSpeed < minMoveSpeedForDirection)
            {
                return desiredTarget;
            }

            float speedLead = Mathf.Min(maxActiveStepSpeedLead, forwardSpeed * activeStepSpeedLeadTime);
            float syncDistance = GaitSyncFactor * forwardSpeed * GetScaledStrideDuration(forwardSpeed);
            float forwardDistance = Mathf.Max(activeStepForwardDistance + speedLead, syncDistance);
            Vector2 target = desiredTarget + travelDirection * forwardDistance;

            // The landing must stay reachable from the hip once the body arrives, or the plant is
            // instantly clamped off the ground and emergency-released, which reads as vibration.
            // The rear-offset leg lands a little shorter so the idle stagger survives at speed.
            float stagger = restFootOffset.x * Mathf.Sign(travelDirection.x);
            float maxAhead = GetMaxForwardLandingOffset() + Mathf.Min(0f, stagger);
            float ahead = Vector2.Dot(target - bodyPosition, travelDirection);
            if (ahead > maxAhead)
            {
                target -= travelDirection * (ahead - maxAhead);
            }

            RaycastHit2D hit = Physics2D.Raycast(
                target + Vector2.up * (probeUp + probeExtra),
                Vector2.down,
                probeUp + probeExtra * 2f + Mathf.Abs(restFootOffset.y),
                groundMask);
            if (!IsValidGroundHit(hit))
            {
                return target;
            }

            desiredNormal = NormalizeOrUp(hit.normal);
            SupportCollider = hit.collider;
            return hit.point + desiredNormal * footSurfaceOffset;
        }

        private void AdvanceStep()
        {
            stepElapsed += GetIkDeltaTime();
            float duration = Mathf.Max(0.01f, activeStepDuration > 0f ? activeStepDuration : strideDuration);
            float t = Mathf.Clamp01(stepElapsed / duration);
            float easedT = SmootherStep(t);
            currentTarget = Vector2.Lerp(stepFrom, stepTo, easedT);
            // Shorter high-cadence steps get a proportionally lower arc so fast gait
            // does not read as vertical vibration.
            float arcScale = Mathf.Clamp(duration / strideDuration, 0.35f, 1f);
            currentTarget.y += Mathf.Sin(Mathf.PI * t) * stepHeight * arcScale;
            Vector2 targetNormal = NormalizeOrUp(Vector2.Lerp(stepNormalFrom, stepNormalTo, easedT));
            currentNormal = SmoothNormal(currentNormal, targetNormal);

            if (t < 1f)
            {
                return;
            }

            PlantState = FootPlantState.Planted;
            plantedTarget = stepTo;
            plantedNormal = NormalizeOrUp(stepNormalTo);
            plantedBodyPosition = GetBodyPosition(GetHipPosition());
            currentTarget = plantedTarget;
            currentNormal = SmoothNormal(currentNormal, plantedNormal);
        }

        private void SnapPlant(Vector2 bodyPosition, Vector2 desiredTarget, Vector2 desiredNormal)
        {
            PlantState = FootPlantState.Planted;
            plantedTarget = desiredTarget;
            plantedNormal = NormalizeOrUp(desiredNormal);
            plantedBodyPosition = bodyPosition;
            currentTarget = plantedTarget;
            currentNormal = plantedNormal;
            airborneVelocity = Vector2.zero;
        }

        private void ResetAfterTeleport(
            Vector2 hip,
            Vector2 bodyPosition,
            bool foundGround,
            Vector2 desiredTarget,
            Vector2 desiredNormal,
            Vector2 restWorld)
        {
            airborneVelocity = Vector2.zero;
            stepElapsed = 0f;

            if (foundGround)
            {
                SnapPlant(bodyPosition, desiredTarget, desiredNormal);
                return;
            }

            PlantState = FootPlantState.Airborne;
            currentTarget = ClampToReach(hip, restWorld);
            currentNormal = Vector2.up;
            plantedTarget = currentTarget;
            plantedNormal = currentNormal;
            plantedBodyPosition = bodyPosition;
        }

        private void UpdateAirborneTarget(Vector2 hip, Vector2 restWorld)
        {
            PlantState = FootPlantState.Airborne;
            Vector2 hangingTarget = ClampToReach(hip, restWorld);
            currentTarget = Vector2.SmoothDamp(
                currentTarget,
                hangingTarget,
                ref airborneVelocity,
                airborneSmoothTime,
                Mathf.Infinity,
                GetIkDeltaTime());
            currentNormal = SmoothNormal(currentNormal, Vector2.up);
        }

        private Vector2 ClampToReach(Vector2 hip, Vector2 point)
        {
            float maxReach = MaxReach;
            float minReach = Mathf.Max(ReachEpsilon, Mathf.Abs(thighLength - shinLength) + ReachEpsilon);
            Vector2 offset = point - hip;
            float distance = offset.magnitude;

            if (distance < 0.0001f)
            {
                return hip + Vector2.down * minReach;
            }

            float clampedDistance = Mathf.Clamp(distance, minReach, maxReach);
            return hip + offset * (clampedDistance / distance);
        }

        private void ResolveAnimatorBones()
        {
            if (!useAnimatorBones || boneAnimator == null)
            {
                return;
            }

            Avatar avatar = boneAnimator.avatar;
            if (avatar == null || !avatar.isHuman)
            {
                return;
            }

            Transform upper = boneAnimator.GetBoneTransform(upperLegBone);
            Transform lower = boneAnimator.GetBoneTransform(lowerLegBone);
            Transform footTransform = boneAnimator.GetBoneTransform(footBone);

            thigh = upper != null ? upper : thigh;
            shin = lower != null ? lower : shin;
            foot = footTransform != null ? footTransform : foot;

            if (upper != null && lower != null)
            {
                thighLength = Mathf.Max(0.01f, Vector2.Distance(upper.position, lower.position));
            }

            if (lower != null && footTransform != null)
            {
                shinLength = Mathf.Max(0.01f, Vector2.Distance(lower.position, footTransform.position));
            }
        }

        private void ResolveStanceSide()
        {
            float localX = transform.localPosition.x;
            if (Mathf.Abs(localX) > 0.001f)
            {
                stanceSideSign = Mathf.Sign(localX);
                return;
            }

            if (Mathf.Abs(restFootOffset.x) > 0.001f)
            {
                stanceSideSign = Mathf.Sign(restFootOffset.x);
                return;
            }

            // White-box left legs use kneeBendSign=1 and right legs use -1 in the scene builder.
            stanceSideSign = kneeBendSign < 0f ? 1f : -1f;
        }

        private Vector2 GetHipPosition()
        {
            return useAnimatorBones && thigh != null ? thigh.position : transform.position;
        }

        private bool IsActualTeleport(Vector2 hip)
        {
            float hipDelta = Vector2.Distance(hip, lastHipPosition);
            if (hipDelta < teleportDistance)
            {
                return false;
            }

            float deltaTime = GetIkDeltaTime();
            if (motionBody != null && deltaTime > 0f)
            {
                float expectedMotion = motionBody.linearVelocity.magnitude * deltaTime;
                return hipDelta > expectedMotion + teleportDistance * 0.5f;
            }

            return hipDelta >= teleportDistance * 2f;
        }

        private Vector2 GetBodyPosition(Vector2 fallback)
        {
            return motionBody != null ? motionBody.position : fallback;
        }

        private void UpdateTravelDirection(Vector2 bodyPosition)
        {
            float deltaTime = GetIkDeltaTime();
            Vector2 velocity = Vector2.zero;
            if (motionBody != null)
            {
                velocity = motionBody.linearVelocity;
            }
            else if (initialized && deltaTime > 0f)
            {
                velocity = (bodyPosition - lastBodyPosition) / deltaTime;
            }

            if (Mathf.Abs(velocity.x) < minMoveSpeedForDirection)
            {
                return;
            }

            Vector2 targetDirection = velocity.x >= 0f ? Vector2.right : Vector2.left;
            float blend = deltaTime > 0f ? 1f - Mathf.Exp(-18f * deltaTime) : 1f;
            travelDirection = NormalizeOrRight(Vector2.Lerp(travelDirection, targetDirection, blend));
        }

        private bool IsValidGroundHit(RaycastHit2D hit)
        {
            if (hit.collider == null)
            {
                return false;
            }

            float minimumUpDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            return Vector2.Dot(hit.normal, Vector2.up) >= minimumUpDot;
        }

        private void ApplyKneeProbeConstraint(Vector2 hip, ref Vector2 target, ref Vector2 normal)
        {
            Vector2 kneeProbe = EstimateKneeProbePosition(hip, target);
            KneeProbePosition = kneeProbe;

            float distance = Mathf.Max(0.01f, Vector2.Distance(kneeProbe, target) + probeExtra);
            RaycastHit2D kneeHit = Physics2D.Raycast(kneeProbe, Vector2.down, distance, groundMask);
            if (IsValidGroundHit(kneeHit))
            {
                SupportCollider = kneeHit.collider;
                normal = NormalizeOrUp(Vector2.Lerp(normal, kneeHit.normal, 0.35f));
            }

            Vector2 kneeToTarget = target - kneeProbe;
            float maxDistanceFromKnee = Mathf.Max(0.01f, shinLength + kneeReachSlack);
            if (kneeToTarget.magnitude <= maxDistanceFromKnee)
            {
                return;
            }

            target = kneeProbe + kneeToTarget.normalized * maxDistanceFromKnee;
            RaycastHit2D reprojection = Physics2D.Raycast(
                target + Vector2.up * probeUp,
                Vector2.down,
                probeUp + probeExtra,
                groundMask);
            if (!IsValidGroundHit(reprojection))
            {
                return;
            }

            normal = NormalizeOrUp(reprojection.normal);
            target = reprojection.point + normal * footSurfaceOffset;
            SupportCollider = reprojection.collider;
        }

        private void ConstrainTargetToBodySide(Vector2 bodyPosition, ref Vector2 target, ref Vector2 normal)
        {
            if (minFootSideOffset <= 0f)
            {
                return;
            }

            float boundary = bodyPosition.x + stanceSideSign * minFootSideOffset;
            bool crossed = stanceSideSign < 0f
                ? target.x > boundary
                : target.x < boundary;
            if (!crossed)
            {
                return;
            }

            target.x = boundary;
            ReprojectTargetToGround(ref target, ref normal);
        }

        private bool IsFootAcrossBody(Vector2 footPosition, Vector2 bodyPosition)
        {
            float boundary = bodyPosition.x + stanceSideSign * minFootSideOffset;
            return stanceSideSign < 0f
                ? footPosition.x > boundary
                : footPosition.x < boundary;
        }

        private void ReprojectTargetToGround(ref Vector2 target, ref Vector2 normal)
        {
            Vector2 origin = target + Vector2.up * (probeUp + probeExtra);
            float distance = probeUp + probeExtra * 2f + Mathf.Abs(restFootOffset.y);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, distance, groundMask);
            if (!IsValidGroundHit(hit))
            {
                return;
            }

            normal = NormalizeOrUp(hit.normal);
            target = hit.point + normal * footSurfaceOffset;
            SupportCollider = hit.collider;
        }

        private Vector2 EstimateKneeProbePosition(Vector2 hip, Vector2 target)
        {
            Vector2 toTarget = target - hip;
            Vector2 upperDirection = toTarget.sqrMagnitude > 0.0001f
                ? toTarget.normalized
                : Vector2.down;
            Vector2 pole = new Vector2(-upperDirection.y, upperDirection.x) * kneeBendSign;
            return hip
                + upperDirection * (thighLength * 0.6f)
                + NormalizeOrZero(pole) * kneeProbeForward;
        }

        private float GetFootTravelAngle(Vector2 from, Vector2 to)
        {
            Vector2 footTravel = to - from;
            if (footTravel.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector2.Angle(footTravel.normalized, travelDirection);
        }

        private float GetAnkleTwistAngle(Vector2 surfaceNormal)
        {
            Vector2 tangent = GetSurfaceTangent(surfaceNormal);
            return Vector2.Angle(tangent, travelDirection);
        }

        private Vector2 GetSurfaceTangent(Vector2 surfaceNormal)
        {
            Vector2 tangent = new Vector2(surfaceNormal.y, -surfaceNormal.x);
            if (Vector2.Dot(tangent, travelDirection) < 0f)
            {
                tangent = -tangent;
            }

            return NormalizeOrRight(tangent);
        }

        private void UpdatePlantWeight(bool plantPulledOffGround)
        {
            float deltaTime = GetIkDeltaTime();
            float targetWeight = IsFootGrounded && !plantPulledOffGround ? 1f : 0f;
            float blend = deltaTime > 0f ? 1f - Mathf.Exp(-plantBlendSpeed * deltaTime) : 1f;
            FootPlantWeight = Mathf.Lerp(FootPlantWeight, targetWeight, blend);
        }

        private float GetForwardSpeed()
        {
            return motionBody != null
                ? Mathf.Abs(Vector2.Dot(motionBody.linearVelocity, travelDirection))
                : 0f;
        }

        private float GetBodySpeed()
        {
            return motionBody != null ? motionBody.linearVelocity.magnitude : 0f;
        }

        private float GetScaledStrideDuration(float forwardSpeed)
        {
            return Mathf.Max(minStrideDuration, strideDuration / (1f + forwardSpeed * cadenceSpeedFactor));
        }

        private bool IsNearlyStationary()
        {
            return GetBodySpeed() <= maxStanceRuleSpeed;
        }

        private float GetMaxForwardLandingOffset()
        {
            // Horizontal placement that keeps a grounded landing inside 97% of leg reach,
            // given the hip stands |restFootOffset.y| above the ground.
            float reach = MaxReach * 0.97f;
            float drop = Mathf.Abs(restFootOffset.y);
            float squared = reach * reach - drop * drop;
            return Mathf.Max(0.12f, squared > 0f ? Mathf.Sqrt(squared) : 0f);
        }

        private float GetEffectiveHipProbeExtra()
        {
            return Mathf.Max(hipProbeExtra, Mathf.Abs(restFootOffset.y) + 0.35f);
        }

        private float GetIkDeltaTime()
        {
            return Time.deltaTime > 0f ? Mathf.Min(Time.deltaTime, maxIkDeltaTime) : 0f;
        }

        private Vector2 SmoothSolvedFootTarget(Vector2 target, bool forceSnap)
        {
            float deltaTime = GetIkDeltaTime();
            if (forceSnap || !hasSmoothedFootPosition || deltaTime <= 0f)
            {
                hasSmoothedFootPosition = true;
                smoothedFootPosition = target;
                return smoothedFootPosition;
            }

            // The cap is relative to the body: it still suppresses single-frame pops, but lets the
            // swing foot overtake a fast-moving body instead of trailing it forever.
            float maxDistance = (maxFootCorrectionSpeed + GetBodySpeed()) * deltaTime;
            smoothedFootPosition = Vector2.MoveTowards(smoothedFootPosition, target, maxDistance);
            return smoothedFootPosition;
        }

        private void Solve(Vector2 hip, Vector2 target, Vector2 surfaceNormal)
        {
            if (thigh == null || shin == null)
            {
                return;
            }

            Vector2 toTarget = target - hip;
            float distance = Mathf.Max(ReachEpsilon, toTarget.magnitude);
            float baseAngle = Mathf.Atan2(toTarget.y, toTarget.x);
            float denominator = 2f * thighLength * distance;
            float cosHipOffset = denominator > 0f
                ? (thighLength * thighLength + distance * distance - shinLength * shinLength) / denominator
                : 1f;
            float hipOffset = Mathf.Acos(Mathf.Clamp(cosHipOffset, -1f, 1f)) * kneeBendSign;

            float thighAngle = baseAngle + hipOffset;
            Vector2 knee = hip + new Vector2(Mathf.Cos(thighAngle), Mathf.Sin(thighAngle)) * thighLength;
            float shinAngle = Mathf.Atan2(target.y - knee.y, target.x - knee.x);

            thigh.position = hip;
            thigh.rotation = Quaternion.Euler(0f, 0f, PointingDownAngle(thighAngle));
            shin.position = knee;
            shin.rotation = Quaternion.Euler(0f, 0f, PointingDownAngle(shinAngle));

            if (foot == null)
            {
                return;
            }

            foot.position = target;
            Quaternion targetFootRotation = Quaternion.LookRotation(
                Vector3.forward,
                new Vector3(surfaceNormal.x, surfaceNormal.y, 0f));
            float targetFootAngle = SignedAngle(targetFootRotation.eulerAngles.z);
            float turnSpeed = Mathf.Min(footTurnSpeed, IsFootGrounded ? plantedFootTurnSpeed : releasedFootTurnSpeed);
            float deltaTime = GetIkDeltaTime();
            float turnBlend = deltaTime > 0f ? 1f - Mathf.Exp(-plantBlendSpeed * deltaTime) : 1f;
            float blendedStep = Mathf.DeltaAngle(currentFootAngle, targetFootAngle) * turnBlend;
            float maxStep = turnSpeed * deltaTime;
            currentFootAngle += Mathf.Clamp(blendedStep, -maxStep, maxStep);
            foot.rotation = Quaternion.Euler(0f, 0f, currentFootAngle);
        }

        private static float PointingDownAngle(float standardAngleRadians)
        {
            Vector2 direction = new Vector2(Mathf.Cos(standardAngleRadians), Mathf.Sin(standardAngleRadians));
            return Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
        }

        private static float SignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value * (value * (value * 6f - 15f) + 10f);
        }

        private Vector2 SmoothNormal(Vector2 from, Vector2 to)
        {
            to = NormalizeOrUp(to);
            from = NormalizeOrUp(from);

            float deltaTime = GetIkDeltaTime();
            if (normalSmoothTime <= 0f || deltaTime <= 0f)
            {
                return to;
            }

            float blend = 1f - Mathf.Exp(-deltaTime / normalSmoothTime);
            return NormalizeOrUp(Vector2.Lerp(from, to, blend));
        }

        private static Vector2 NormalizeOrUp(Vector2 value)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector2.up;
        }

        private static Vector2 NormalizeOrRight(Vector2 value)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector2.right;
        }

        private static Vector2 NormalizeOrZero(Vector2 value)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector2.zero;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 hip = GetHipPosition();
            Vector2 restWorld = hip + (Vector2)transform.TransformVector(restFootOffset);
            Vector2 origin = restWorld + Vector2.up * probeUp;
            Vector2 hipOrigin = hip + Vector2.up * probeUp;

            Gizmos.color = IsFootGrounded ? Color.green : new Color(1f, 0.55f, 0.1f);
            Gizmos.DrawLine(origin, origin + Vector2.down * (probeUp + probeExtra));
            Gizmos.DrawWireSphere(FootWorldPosition, 0.05f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(hipOrigin, hipOrigin + Vector2.down * (probeUp + Mathf.Abs(restFootOffset.y) + GetEffectiveHipProbeExtra()));
            Gizmos.color = Color.magenta;
            Vector2 kneeProbe = initialized ? KneeProbePosition : EstimateKneeProbePosition(hip, restWorld);
            Gizmos.DrawLine(kneeProbe, kneeProbe + Vector2.down * (shinLength + kneeReachSlack));
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hip, MaxReach);
        }
    }
}
