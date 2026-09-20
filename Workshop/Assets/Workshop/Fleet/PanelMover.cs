using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// Quest-home-style movement for a panel. Point the hand ray at the
    /// title bar and pinch: the panel rides the ray. Push the hand forward
    /// or pull it back and the panel goes farther or nearer — amplified, so
    /// a small push sends a panel across the room — and it turns to face
    /// you as it moves. Input comes from the Interaction SDK's RayInteractor
    /// (origin and direction); the movement is ours.
    /// </summary>
    public sealed class PanelMover : MonoBehaviour
    {
        /// <summary>Hand travel along the ray → panel distance change.</summary>
        public float pushGain = 4f;
        public float minDistance = 0.3f;
        public float maxDistance = 5f;
        /// <summary>Seconds to settle onto the target pose.</summary>
        public float smoothing = 0.08f;

        public event Action Grabbed;
        public event Action Released;
        public bool IsGrabbed => _ray != null;

        private static RayInteractor[] _rays;
        private RayInteractor _ray;
        private float _distance;
        private Vector3 _handStart;
        private Transform _handle;
        private Transform _head;
        private Vector3 _targetPos;
        private Quaternion _targetRot;
        private Vector3 _velocity;
        private bool _settling;

        /// <summary>Wire to the title bar's pointable element and remember where the bar sits.</summary>
        public void Attach(PointableElement handle, Transform handleTransform)
        {
            _handle = handleTransform;
            handle.WhenPointerEventRaised += OnPointer;
            _head = Camera.main ? Camera.main.transform : null;
        }

        private void OnPointer(PointerEvent e)
        {
            switch (e.Type)
            {
                case PointerEventType.Select:
                    if (_ray != null) return;
                    var ray = FindRay(e.Identifier);
                    if (ray == null) return;
                    _ray = ray;
                    _distance = Vector3.Distance(ray.Origin, e.Pose.position);
                    _handStart = ray.Origin;
                    _targetPos = transform.position;
                    _targetRot = transform.rotation;
                    _settling = false;
                    Grabbed?.Invoke();
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (_ray == null || e.Identifier != _ray.Identifier) return;
                    _ray = null;
                    _settling = true;
                    Released?.Invoke();
                    break;
            }
        }

        private void Update()
        {
            if (_ray != null)
            {
                // Distance: where the bar was, plus amplified push/pull.
                var travel = Vector3.Dot(_ray.Origin - _handStart, _ray.Forward);
                var d = Mathf.Clamp(_distance + travel * pushGain, minDistance, maxDistance);
                var barPoint = _ray.Origin + _ray.Forward * d;
                _targetRot = Facing(barPoint);
                // The panel's centre sits at a fixed offset from the bar.
                var barOffset = _handle ? _handle.position - transform.position : Vector3.zero;
                var localOffset = Quaternion.Inverse(transform.rotation) * barOffset;
                _targetPos = barPoint - _targetRot * localOffset;
            }
            if (_ray != null || _settling)
            {
                transform.position = Vector3.SmoothDamp(transform.position, _targetPos, ref _velocity, smoothing);
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, smoothing)));
                if (_settling && (transform.position - _targetPos).sqrMagnitude < 1e-6f)
                {
                    transform.SetPositionAndRotation(_targetPos, _targetRot);
                    _settling = false;
                }
            }
        }

        /// <summary>Upright, turned to face the head (yaw only, like a window).</summary>
        public Quaternion Facing(Vector3 at)
        {
            var from = _head ? _head.position : Vector3.zero;
            var dir = Vector3.ProjectOnPlane(at - from, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) return transform.rotation;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>Turn to face the head from where it is now, smoothly.</summary>
        public void FaceMe()
        {
            _targetPos = transform.position;
            _targetRot = Facing(transform.position);
            _settling = true;
        }

        private static RayInteractor FindRay(int identifier)
        {
            if (_rays == null || _rays.Length == 0 || Array.Exists(_rays, r => r == null))
                _rays = FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in _rays)
                if (r && r.Identifier == identifier) return r;
            return null;
        }
    }
}
