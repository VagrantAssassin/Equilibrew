using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 5f;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 newPos = transform.position;
        newPos.x = Mathf.Lerp(transform.position.x, target.position.x, smoothSpeed * Time.deltaTime);
        transform.position = newPos;
    }
}
