using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.Serialization;

namespace CriticalAngleStudios
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class Player : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 6.0f;
        [SerializeField] private float crouchSpeed = 3.0f;

        [Space] [SerializeField] private float timeToCrouch = 0.2f;
        [SerializeField] private float cameraStandingHeight = 0.75f;
        [SerializeField] private float cameraCrouchHeight = 0.25f;

        [Space] [SerializeField] private float maxSlopeAngle = 45.0f;
        [SerializeField] private float jumpHeight = 1.0f;
        [SerializeField] private float maxAirAcceleration = 1.0f;
        [SerializeField] private float airAcceleration = 10.0f;
        [SerializeField] private float rampSlideVelocity = 5.0f;
        [SerializeField] private bool canHoldJump;

        [Space] [SerializeField] private float cameraSensitivity = 1.0f;
        [SerializeField] private new Transform camera;
        [SerializeField] private new CapsuleCollider collider;
        [SerializeField] private LayerMask groundMask;

        private new Rigidbody rigidbody;

        private bool isGrounded;
        private bool wasGrounded;
        private readonly Dictionary<GameObject, Vector3> collisions = new();

        private Vector2 inputRotation;
        private float desiredSpeed;
        private Vector3 groundNormal;

        private bool shouldJump;

        private bool isCrouched;
        private bool isTransitioningCrouch;
        private bool shouldCancelCrouchTransition;

        private void Awake()
        {
            this.rigidbody = this.GetComponent<Rigidbody>();

            this.isGrounded = true;

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            this.SetDesiredSpeed();
            this.SetDesiredRotation();

            if (this.isGrounded)
            {
                if (this.JumpInput())
                    this.shouldJump = true;

                if (!this.isTransitioningCrouch)
                {
                    if (this.CrouchInput()
                        && !this.isCrouched)
                        this.StartCoroutine(this.Crouch());
                    else if (!this.CrouchInput()
                             && this.isCrouched)
                        this.StartCoroutine(this.UnCrouch());
                }
            }
            else
            {
                if (this.CrouchInput() && !this.isCrouched)
                    this.AirCrouch();
                else if (!this.CrouchInput() && this.isCrouched)
                    this.AirUnCrouch();
            }

            this.camera.transform.localEulerAngles = new Vector3(this.inputRotation.x, 0.0f, 0.0f);
            this.rigidbody.MoveRotation(Quaternion.Euler(0.0f, this.inputRotation.y, 0.0f));
        }

        private void FixedUpdate()
        {
            this.GroundCheck();

            if (this.isGrounded && !this.wasGrounded && this.isCrouched)
                this.AirCrouchToCrouch();

            if (this.shouldJump && this.isGrounded)
            {
                this.Jump();
            }
            else
            {
                if (!this.isGrounded && this.wasGrounded && this.isCrouched)
                    this.CrouchToAirCrouch();

                if (this.isGrounded && this.wasGrounded)
                    this.GroundAccelerate();
                else
                    this.AirAccelerate();
            }

            this.wasGrounded = this.isGrounded;
        }

        private void OnCollisionEnter(Collision collision) =>
            this.collisions.Add(collision.gameObject, collision.GetContact(0).normal);

        private void OnCollisionStay(Collision collision) =>
            this.collisions[collision.gameObject] = collision.GetContact(0).normal;

        private void OnCollisionExit(Collision collision) =>
            this.collisions.Remove(collision.gameObject);

        private void GroundCheck()
        {
            // Replication of Quake's undocumented feature of ramp sliding when exceeding a certain velocity
            if (this.rigidbody.velocity.y > this.rampSlideVelocity)
            {
                this.isGrounded = false;
                return;
            }

            // Iterates through every object that we are colliding with to get the average ground normal
            var combinedNormals = Vector3.zero;
            var isGroundedTmp = false;
            foreach (var (obj, normal) in this.collisions)
            {
                if ((this.groundMask & (1 << obj.layer)) == 0) continue;

                var angle = Vector3.Angle(Vector3.up, normal);
                if (angle > this.maxSlopeAngle) continue;

                combinedNormals += normal;
                isGroundedTmp = true;
            }

            this.groundNormal = combinedNormals;
            this.isGrounded = isGroundedTmp;
        }

        private Vector3 GetInputDirection()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0.0f, Input.GetAxisRaw("Vertical"));
            input.Normalize();

            return this.transform.TransformDirection(input);
        }

        private void SetDesiredSpeed() =>
            this.desiredSpeed = this.isCrouched ? this.crouchSpeed : this.walkSpeed;

        private void SetDesiredRotation()
        {
            this.inputRotation.x -= Input.GetAxis("Mouse Y") * this.cameraSensitivity;
            this.inputRotation.y += Input.GetAxis("Mouse X") * this.cameraSensitivity;

            this.inputRotation.x = Mathf.Clamp(this.inputRotation.x, -90.0f, 90.0f);
        }

        private void GroundAccelerate()
        {
            // Stop the player from sliding down ramps

            // Gets the direction up the ramp
            var balanceForce = Vector3.down - Vector3.Project(Vector3.down, this.groundNormal);
            balanceForce.Normalize();

            // Uses trig to calculate the effect of gravity on the player on any given ramp
            var angle = Vector3.Angle(Vector3.down, this.groundNormal) * Mathf.Deg2Rad;
            balanceForce *= Physics.gravity.y * Mathf.Sin(angle);
            this.rigidbody.AddForce(balanceForce, ForceMode.Acceleration);

            // Calculates acceleration needed to maintain speed using physics equations
            // acceleration = (desired velocity - previous velocity) / delta time
            var force = (this.desiredSpeed * this.GetInputDirection() - this.rigidbody.velocity) / Time.deltaTime;
            force.y = 0.0f;

            // Walk up ramps
            // proj_normal force = (force * normal) / ||normal||^2 * normal
            var normalProjection =
                Vector3.Dot(force, this.groundNormal) / this.groundNormal.sqrMagnitude * this.groundNormal;
            var proj = force - normalProjection;

            if (!float.IsNaN(proj.x) && !float.IsNaN(proj.y) && !float.IsNaN(proj.z))
                this.rigidbody.AddForce(proj);
        }


        private void AirAccelerate()
        {
            // Don't even dare force me to explain this because I cannot
            // This was taken right from the Source Engine repository
            var direction = this.GetInputDirection();
            var velocity = this.rigidbody.velocity;
            var magnitude = velocity.magnitude;

            var wishSpeed = magnitude;

            if (wishSpeed > this.maxAirAcceleration)
                wishSpeed = this.maxAirAcceleration;

            var currentSpeed = Vector3.Dot(velocity, direction);

            var addSpeed = wishSpeed - currentSpeed;
            if (addSpeed <= 0)
                return;

            var accelerationSpeed = this.airAcceleration * magnitude * Time.deltaTime;

            if (accelerationSpeed > addSpeed)
                accelerationSpeed = addSpeed;

            var addVelocity = accelerationSpeed * direction;
            if (this.isGrounded)
                addVelocity = Vector3.ProjectOnPlane(addVelocity, this.groundNormal);

            this.rigidbody.AddForce(addVelocity, ForceMode.VelocityChange);
        }

        private void Jump()
        {
            this.shouldJump = false;
            if (this.isCrouched) return;

            if (this.isTransitioningCrouch)
                this.HandleJumpFromCrouch();

            var height = this.jumpHeight;

            // Simple calculation for jump force
            var force = Mathf.Sqrt(height * -2.0f * Physics.gravity.y) - this.rigidbody.velocity.y;
            this.rigidbody.AddForce(0.0f, force, 0.0f, ForceMode.VelocityChange);
        }

        // These next few functions are self-documenting
        private void HandleJumpFromCrouch()
        {
            this.shouldCancelCrouchTransition = true;
            this.camera.localPosition = new Vector3(0.0f, this.cameraStandingHeight);

            this.isTransitioningCrouch = false;
            this.AirCrouch();
        }

        private void AirCrouch()
        {
            this.collider.height = 1.5f;
            this.collider.center = new Vector3(0.0f, 0.25f);

            if (this.isTransitioningCrouch)
                this.shouldCancelCrouchTransition = true;

            this.isCrouched = true;
        }

        private void AirUnCrouch()
        {
            if (Physics.SphereCast(this.transform.position + this.collider.center, this.collider.radius - 0.001f,
                    Physics.gravity.normalized, out _, 0.75f)) return;

            this.collider.height = 2.0f;
            this.collider.center = Vector3.zero;

            this.isCrouched = false;
        }

        private void AirCrouchToCrouch()
        {
            this.rigidbody.position += new Vector3(0.0f, 0.5f);
            this.collider.center = new Vector3(0.0f, -0.25f);
            this.camera.localPosition = new Vector3(0.0f, this.cameraCrouchHeight);
        }

        private void CrouchToAirCrouch()
        {
            this.rigidbody.position -= new Vector3(0.0f, 0.5f);
            this.collider.center = new Vector3(0.0f, 0.25f);
            this.camera.localPosition = new Vector3(0.0f, this.cameraStandingHeight);
        }

        private IEnumerator Crouch()
        {
            var time = 0.0f;
            this.isTransitioningCrouch = true;

            while (time <= this.timeToCrouch)
            {
                // If, while we are in the process of crouching, we fall off a cliff, transition to an air crouch
                if (!this.isGrounded)
                {
                    this.camera.localPosition = new Vector3(0.0f, this.cameraStandingHeight);
                    this.AirCrouch();
                }

                if (this.shouldCancelCrouchTransition)
                {
                    this.shouldCancelCrouchTransition = false;
                    yield break;
                }

                var lerp = Mathf.Lerp(this.cameraStandingHeight, this.cameraCrouchHeight,
                    EaseInOut(time / this.timeToCrouch));
                this.camera.localPosition = new Vector3(0.0f, lerp, 0.0f);

                time += Time.deltaTime;

                yield return null;
            }

            this.collider.height = 1.5f;
            this.collider.center = new Vector3(0.0f, -0.25f);

            this.isTransitioningCrouch = false;
            this.isCrouched = true;
        }

        private IEnumerator UnCrouch()
        {
            var time = 0.0f;
            this.isTransitioningCrouch = true;

            while (time <= this.timeToCrouch)
            {
                if (this.shouldCancelCrouchTransition)
                {
                    this.shouldCancelCrouchTransition = false;
                    yield break;
                }

                var lerp = Mathf.Lerp(this.cameraCrouchHeight, this.cameraStandingHeight,
                    EaseInOut(time / this.timeToCrouch));
                this.camera.localPosition = new Vector3(0.0f, lerp, 0.0f);

                time += Time.deltaTime;

                yield return null;
            }

            this.collider.height = 2.0f;
            this.collider.center = Vector3.zero;

            this.isTransitioningCrouch = false;
            this.isCrouched = false;
        }

        private static float EaseInOut(float x) => -(Mathf.Cos(Mathf.PI * x) - 1.0f) / 2.0f;

        private bool JumpInput() => this.canHoldJump ? Input.GetKey(KeyCode.Space) : Input.GetKeyDown(KeyCode.Space);
        private bool CrouchInput() => Input.GetKey(KeyCode.LeftControl);
    }
}