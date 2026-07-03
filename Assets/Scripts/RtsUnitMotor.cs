using UnityEngine;

public class RtsUnitMotor : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private bool isCommandUnit = true;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float stoppingDistance = 0.05f;

    private bool hasDestination;
    private Vector3 destination;

    public bool IsCommandUnit => isCommandUnit;
    public bool CanReceiveCommands { get; set; } = true;
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public bool HasDestination => hasDestination;

    public void MoveTo(Vector3 worldPoint)
    {
        destination = new Vector3(worldPoint.x, transform.position.y, worldPoint.z);
        hasDestination = true;
    }

    public void Stop()
    {
        hasDestination = false;
    }

    private void Update()
    {
        if (!hasDestination)
        {
            return;
        }

        Vector3 offset = destination - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            hasDestination = false;
            return;
        }

        Vector3 direction = offset.normalized;
        float stepDistance = moveSpeed * Mathf.Max(0f, MoveSpeedMultiplier) * Time.deltaTime;

        if (stepDistance * stepDistance >= offset.sqrMagnitude)
        {
            transform.position = destination;
            hasDestination = false;
            return;
        }

        transform.position += direction * stepDistance;
    }
}
