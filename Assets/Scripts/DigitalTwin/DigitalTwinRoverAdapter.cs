using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinRoverAdapter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private Transform roverTransform;
        [SerializeField] private bool createRoverIfMissing = true;

        [Header("Pose")]
        [SerializeField] private bool applyYaw = true;
        [SerializeField] private bool smoothPoseUpdates = true;
        [SerializeField] private float positionLerpSpeed = 10f;
        [SerializeField] private float rotationLerpSpeed = 8f;
        [SerializeField] private float fallbackAltitude = 0.2f;

        private bool _hasTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;

        public bool HasRover => roverTransform != null;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (roverTransform == null)
                ResolveRoverTransform();
            if (roverTransform == null && createRoverIfMissing)
                CreateFallbackRover();
        }

        private void Update()
        {
            if (!smoothPoseUpdates || !_hasTarget || roverTransform == null)
                return;

            float dt = Time.unscaledDeltaTime;
            float posT = 1f - Mathf.Exp(-Mathf.Max(0.01f, positionLerpSpeed) * dt);
            float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationLerpSpeed) * dt);
            roverTransform.position = Vector3.Lerp(roverTransform.position, _targetPosition, posT);
            roverTransform.rotation = Quaternion.Slerp(roverTransform.rotation, _targetRotation, rotT);
        }

        public bool TryApplyPose(TwinPoseBlock pose)
        {
            if (pose == null || abstractMap == null)
                return false;
            if (roverTransform == null)
            {
                ResolveRoverTransform();
                if (roverTransform == null && createRoverIfMissing)
                    CreateFallbackRover();
            }
            if (roverTransform == null)
                return false;

            try
            {
                var geo = new Vector2d(pose.latitude, pose.longitude);
                var world = abstractMap.GeoToWorldPosition(geo, true);
                if (pose.altitudeM > 0.01f)
                    world.y = pose.altitudeM;
                else if (abstractMap.Root != null)
                    world.y = abstractMap.Root.position.y + fallbackAltitude;

                var rot = roverTransform.rotation;
                if (applyYaw)
                    rot = Quaternion.Euler(0f, pose.yawDeg, 0f);

                if (smoothPoseUpdates)
                {
                    _targetPosition = world;
                    _targetRotation = rot;
                    _hasTarget = true;
                }
                else
                {
                    roverTransform.position = world;
                    roverTransform.rotation = rot;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryApplySlamPose(TwinSlamPoseBlock slamPose)
        {
            if (slamPose == null)
                return false;
            var pose = new TwinPoseBlock
            {
                latitude = slamPose.latitude,
                longitude = slamPose.longitude,
                altitudeM = slamPose.altitudeM,
                yawDeg = slamPose.yawDeg
            };
            return TryApplyPose(pose);
        }

        private void ResolveRoverTransform()
        {
            foreach (var t in FindObjectsOfType<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("rover") || n.Contains("ika") || n.Contains("ugv"))
                {
                    roverTransform = t;
                    return;
                }
            }
        }

        private void CreateFallbackRover()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DigitalTwinRover";
            go.transform.localScale = new Vector3(1.1f, 0.5f, 1.8f);
            if (abstractMap != null && abstractMap.Root != null)
                go.transform.position = abstractMap.Root.position + Vector3.up * fallbackAltitude;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = new Color(0.95f, 0.75f, 0.2f, 1f);
                r.material = mat;
            }
            roverTransform = go.transform;
        }
    }
}
