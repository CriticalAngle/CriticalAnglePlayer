using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace CriticalAngleStudios
{
    [RequireComponent(typeof(Rigidbody))]
    public class Player : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 6.0f;
        [SerializeField] private float crouchSpeed = 5.0f;
        
        [Space]
        
        [SerializeField] private float timeToCrouch = 0.15f;
        [SerializeField] private float standingHeight = 0.75f;
        [SerializeField] private float crouchHeight = 0.25f;
        [SerializeField] private float cameraOffset = -0.25f;
        
        [Space]
        
        [SerializeField] private float maxSlopeAngle = 45.0f;
        [SerializeField] private float jumpHeight = 1.0f;
        [SerializeField] private float crouchJumpMultiplier = 1.5f;
        [SerializeField] private float maxAirAcceleration = 1.0f;
        [SerializeField] private float airAcceleration = 10.0f;
        
        [Space]
        
        [SerializeField] private float sensitivity = 1.0f;
        [SerializeField] private new Transform camera;

        private new Rigidbody rigidbody;

        private bool isGrounded;
        private bool wasGrounded;
        
        private Vector2 inputRotation;
        private float desiredSpeed;
        private Vector3 groundNormal;
        private bool shouldJump;
        private bool waitUntilGrounded;
        private readonly Dictionary<GameObject, Vector3> collisions = new();
        private bool isFullyCrouched;
        private bool transitioningCrouch;

        private void Awake()
        {
            this.rigidbody = this.GetComponent<Rigidbody>();

            this.isGrounded = true;
        
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            this.GetDesiredSpeed();
            this.GetDesiredRotation();

            if (this.IsJumpingInput())
                this.shouldJump = this.isGrounded;

            if (this.IsCrouchingInput() && !this.isFullyCrouched && !this.transitioningCrouch && this.isGrounded)
                this.StartCoroutine(this.Crouch());
            else if (!this.IsCrouchingInput() && this.isFullyCrouched && !this.transitioningCrouch)
                this.StartCoroutine(this.UnCrouch());

            this.camera.transform.localEulerAngles = new Vector3(this.inputRotation.x, 0.0f, 0.0f);
            this.rigidbody.MoveRotation(Quaternion.Euler(0.0f, this.inputRotation.y, 0.0f));
        }

        private void FixedUpdate()
        {
            this.GroundCheck();

            if (this.shouldJump)
                this.Jump();
            else
            {
                var balanceForce = Vector3.down - Vector3.Project(Vector3.down, this.groundNormal);
                balanceForce.Normalize();
        
                var angle = Vector3.Angle(Vector3.down, this.groundNormal) * Mathf.Deg2Rad;
                balanceForce *= Physics.gravity.y * Mathf.Sin(angle);
                this.rigidbody.AddForce(balanceForce, ForceMode.Acceleration);
                
                if (this.isGrounded)
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
            var newNormal = Vector3.zero;
            var isGroundedTmp = false;
            foreach (var (_, normal) in this.collisions)
            {
                var angle = Vector3.Angle(Vector3.up, normal);
                if (angle > this.maxSlopeAngle) continue;
            
                newNormal += normal;
                isGroundedTmp = true;
            }

            this.groundNormal = newNormal;
            this.isGrounded = isGroundedTmp;

            if (this.waitUntilGrounded && this.isGrounded && !this.wasGrounded)
                this.waitUntilGrounded = false;
        }

        private Vector3 GetInputDirection()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0.0f, Input.GetAxisRaw("Vertical"));
            input.Normalize();

            return this.transform.TransformDirection(input);
        }

        private void GetDesiredSpeed()
        {
            this.desiredSpeed = this.isFullyCrouched ? this.crouchSpeed : this.walkSpeed;
        }

        private void GetDesiredRotation()
        {
            this.inputRotation.x -= Input.GetAxis("Mouse Y") * this.sensitivity;
            this.inputRotation.y += Input.GetAxis("Mouse X") * this.sensitivity;
            
            this.inputRotation.x = Mathf.Clamp(this.inputRotation.x, -90.0f, 90.0f);
        }

        private void GroundAccelerate()
        {
            // acceleration = (desired velocity - previous velocity) / delta time
            var force = (this.desiredSpeed * this.GetInputDirection() - this.rigidbody.velocity) / Time.deltaTime;
            force.y = 0.0f;

            // proj_normal force = (force * normal) / ||normal||^2 * normal
            var normalProjection =
                Vector3.Dot(force, this.groundNormal) / this.groundNormal.sqrMagnitude * this.groundNormal;
            var proj = force - normalProjection;
        
            if (!float.IsNaN(proj.x) && !float.IsNaN(proj.y) && !float.IsNaN(proj.z)) 
                this.rigidbody.AddForce(proj);
        }
    
        private void AirAccelerate()
        {
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
            if (!this.isGrounded || this.waitUntilGrounded || this.transitioningCrouch) return;
            
            this.waitUntilGrounded = true;

            var height = this.jumpHeight;
            
            if (this.isFullyCrouched)
            {
                this.StartCoroutine(this.UnCrouch());
                height *= this.crouchJumpMultiplier;
            }

            var force = Mathf.Sqrt(height * -2.0f * Physics.gravity.y) - this.rigidbody.velocity.y;
            this.rigidbody.AddForce(0.0f, force, 0.0f, ForceMode.VelocityChange);
        }

        private IEnumerator Crouch()
        {
            var time = 0.0f;
            this.transitioningCrouch = true;
            
            while (time <= this.timeToCrouch)
            {
                var lerp = Mathf.Lerp(this.standingHeight, this.crouchHeight, EaseInOut(time / this.timeToCrouch));
                this.camera.transform.localPosition = new Vector3(0.0f, lerp + this.cameraOffset, 0.0f);

                time += Time.deltaTime;

                yield return null;
            }
            
            this.transitioningCrouch = false;
            this.isFullyCrouched = true;
        }
        
        private IEnumerator UnCrouch()
        {
            var time = 0.0f;
            this.transitioningCrouch = true;
            
            while (time <= this.timeToCrouch)
            {
                var lerp = Mathf.Lerp(this.crouchHeight, this.standingHeight, EaseInOut(time / this.timeToCrouch));
                this.camera.transform.localPosition = new Vector3(0.0f, lerp + this.cameraOffset, 0.0f);

                time += Time.deltaTime;

                yield return null;
            }
            
            this.transitioningCrouch = false;
            this.isFullyCrouched = false;
        }

        private static float EaseInOut(float x)
        {
            return -(Mathf.Cos(Mathf.PI * x) - 1.0f) / 2.0f;
        }

        private bool IsJumpingInput()
        {
            return Input.GetKey(KeyCode.Space);
        }
    
        private bool IsCrouchingInput()
        {
            return Input.GetKey(KeyCode.LeftControl);
        }
    }
}
