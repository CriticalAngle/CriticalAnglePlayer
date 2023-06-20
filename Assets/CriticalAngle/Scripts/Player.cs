using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private PlayerInputControls inputControls;

        private bool isGrounded;
        private bool wasGrounded;
        private readonly Dictionary<GameObject, Vector3> collisions = new();

        private Vector2 rotationInput;
        private bool crouchInput;
        private bool jumpInput;
        
        private float desiredSpeed;
        private Vector3 groundNormal;

        private bool shouldJump;

        private bool isCrouched;
        private bool isTransitioningCrouch;
        private bool shouldCancelCrouchTransition;

        private void Awake()
        {
            this.rigidbody = this.GetComponent<Rigidbody>();

            this.inputControls = new PlayerInputControls();
            this.inputControls.Enable();

            this.inputControls.Player.Jump.performed += _ =>
            {
                this.shouldJump = true;
                this.jumpInput = true;
            };
            this.inputControls.Player.Jump.canceled += _ => this.jumpInput = false;
            
            this.inputControls.Player.Crouch.performed += _ => this.crouchInput = true;
            this.inputControls.Player.Crouch.canceled += _ => this.crouchInput = false;

            this.isGrounded = true;

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDestroy()
        {
            this.inputControls.Disable();
            this.inputControls.Dispose();
        }

        private void Update()
        {
            this.SetDesiredSpeed();
            this.SetDesiredRotation();

            if (this.isGrounded)
            {
                if (this.canHoldJump && this.jumpInput)
                    this.shouldJump = true;
                
                if (!this.isTransitioningCrouch)
                {
                    if (this.crouchInput
                        && !this.isCrouched)
                        this.StartCoroutine(this.Crouch());
                    else if (!this.crouchInput
                             && this.isCrouched)
                        this.StartCoroutine(this.UnCrouch());
                }
            }
            else
            {
                if (this.crouchInput && !this.isCrouched)
                    this.AirCrouch();
                else if (!this.crouchInput && this.isCrouched)
                    this.AirUnCrouch();

                this.shouldJump = false;
            }

            this.camera.transform.localEulerAngles = new Vector3(this.rotationInput.x, 0.0f, 0.0f);
            this.rigidbody.MoveRotation(Quaternion.Euler(0.0f, this.rotationInput.y, 0.0f));
        }

        private void FixedUpdate()
        {
            this.GroundCheck();

            if (this.isGrounded && !this.wasGrounded)
            {
                if (this.isCrouched)
                    this.AirCrouchToCrouch();

                var groundAngle = Vector3.Angle(Vector3.up, this.groundNormal);
                var magnitude = this.rigidbody.mass * Mathf.Sin(groundAngle * Mathf.Deg2Rad) * -Physics.gravity.y;
                var force = magnitude * this.groundNormal;
                this.rigidbody.AddForce(-force);
            }

            if (this.shouldJump && this.isGrounded && !this.isCrouched)
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
            var movement = this.inputControls.Player.Movement.ReadValue<Vector2>();
            var input = new Vector3(movement.x, 0.0f, movement.y);

            return this.transform.TransformDirection(input);
        }

        private void SetDesiredSpeed() =>
            this.desiredSpeed = this.isCrouched ? this.crouchSpeed : this.walkSpeed;

        private void SetDesiredRotation()
        {
            var look = this.inputControls.Player.Look.ReadValue<Vector2>();
            this.rotationInput.x -= look.y * this.cameraSensitivity;
            this.rotationInput.y += look.x * this.cameraSensitivity;

            this.rotationInput.x = Mathf.Clamp(this.rotationInput.x, -90.0f, 90.0f);
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

            var height = this.jumpHeight;

            // Simple calculation for jump force
            var force = Mathf.Sqrt(height * -2.0f * Physics.gravity.y) - this.rigidbody.velocity.y;
            this.rigidbody.AddForce(0.0f, force, 0.0f, ForceMode.VelocityChange);
        }

        // These next few functions are self-documenting
        private void AirCrouch()
        {
            this.collider.height = 1.5f;
            this.collider.center = new Vector3(0.0f, 0.25f);

            if (this.isTransitioningCrouch)
                this.shouldCancelCrouchTransition = true;

            this.isTransitioningCrouch = false;
            this.isCrouched = true;
        }

        private void AirUnCrouch()
        {
            if (Physics.SphereCast(this.transform.position + this.collider.center, this.collider.radius - 0.001f,
                    Physics.gravity.normalized, out _, 0.75f)) return;

            this.collider.height = 2.0f;
            this.collider.center = Vector3.zero;

            if (this.isTransitioningCrouch)
                this.shouldCancelCrouchTransition = true;

            this.isTransitioningCrouch = false;
            this.isCrouched = false;
        }

        private void AirCrouchToCrouch()
        {
            this.transform.position += new Vector3(0.0f, 0.5f);
            this.collider.center = new Vector3(0.0f, -0.25f);
            this.camera.localPosition = new Vector3(0.0f, this.cameraCrouchHeight);
        }

        private void CrouchToAirCrouch()
        {
            this.transform.position -= new Vector3(0.0f, 0.5f);
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
                    var difference = this.cameraStandingHeight - this.camera.localPosition.y;
                    this.camera.localPosition = new Vector3(0.0f, this.cameraStandingHeight);
                    this.transform.position -= new Vector3(0.0f, difference);
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
                // If, while we are in the process of un-crouching, we fall off a cliff, transition to airborne standing
                if (!this.isGrounded)
                {
                    this.camera.localPosition = new Vector3(0.0f, this.cameraStandingHeight);
                    this.AirUnCrouch();
                }
                
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
    }
}