using UnityEngine;
using UnityEngine.InputSystem; // Required to read the new Input System

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f; // Adjust this in Inspector to change speed
    private Vector2 moveInput;
    private Rigidbody rb;

    private void Start()
    {
        // Automatically find the physics Rigidbody component on this object
        rb = GetComponent<Rigidbody>();
    }

    // This method triggers automatically when the Left Stick moves
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    private void FixedUpdate()
    {
        if (rb == null) return;
        // Convert the 2D joystick input into a 3D movement direction
        Vector3 movement = new Vector3(moveInput.x, 0f, moveInput.y);

        // Move the Rigidbody physics object smoothly across the ground
        rb.MovePosition(transform.position + movement * moveSpeed * Time.fixedDeltaTime);
    }
}
