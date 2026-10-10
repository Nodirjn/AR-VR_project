using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float cameraSmoothTime = 0.12f;

    private Rigidbody body;
    private Transform followCamera;
    private Vector3 cameraOffset;
    private Vector3 cameraVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            followCamera = mainCamera.transform;
            cameraOffset = followCamera.position - transform.position;
        }
    }

    private void FixedUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;
        }

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        Vector3 velocity = direction * moveSpeed;
        body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);
    }

    private void LateUpdate()
    {
        if (followCamera == null)
            return;

        Vector3 targetPosition = transform.position + cameraOffset;
        followCamera.position = Vector3.SmoothDamp(
            followCamera.position, targetPosition, ref cameraVelocity, cameraSmoothTime);
        followCamera.LookAt(transform.position + Vector3.up * 0.5f);
    }
}
