using UnityEngine;

/// <summary>
/// Animates a city gate up while troops enter or leave through it, and keeps it closed otherwise.
/// Attach to the moving gate mesh/pivot and assign the transform that should rise on the local Y axis.
/// </summary>
public class RtsCityGateController : MonoBehaviour
{
    [Header("Gate")]
    [SerializeField] private Transform gateTransform;
    [SerializeField] private TroopCombat.Faction gateFaction = TroopCombat.Faction.Enemy;

    [Header("Animation")]
    [Tooltip("How far the gate rises on local Y when open.")]
    [SerializeField, Min(0f)] private float openHeight = 4f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 6f;
    [SerializeField, Min(0.05f)] private float troopScanInterval = 0.15f;

    private Vector3 closedLocalPosition;
    private float openLocalY;
    private float currentOpenAmount;
    private float nextTroopScanTime;
    private bool shouldBeOpen;

    private void Awake()
    {
        if (gateTransform == null)
        {
            gateTransform = transform;
        }

        closedLocalPosition = gateTransform.localPosition;
        openLocalY = closedLocalPosition.y + openHeight;
    }

    private void OnValidate()
    {
        openHeight = Mathf.Max(0f, openHeight);
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        troopScanInterval = Mathf.Max(0.05f, troopScanInterval);
    }

    private void Update()
    {
        if (Time.time >= nextTroopScanTime)
        {
            nextTroopScanTime = Time.time + troopScanInterval;
            shouldBeOpen = EvaluateShouldBeOpen();
        }

        float targetOpenAmount = shouldBeOpen ? 1f : 0f;
        currentOpenAmount = Mathf.MoveTowards(currentOpenAmount, targetOpenAmount, moveSpeed * Time.deltaTime);

        Vector3 localPosition = gateTransform.localPosition;
        localPosition.y = Mathf.Lerp(closedLocalPosition.y, openLocalY, currentOpenAmount);
        gateTransform.localPosition = localPosition;
    }

    private bool EvaluateShouldBeOpen()
    {
        TroopCombat[] troops = FindObjectsByType<TroopCombat>(FindObjectsSortMode.None);
        for (int i = 0; i < troops.Length; i++)
        {
            TroopCombat troop = troops[i];
            if (troop == null || troop.TroopFaction != gateFaction || troop.CurrentState == TroopCombat.State.Dead)
            {
                continue;
            }

            if (troop.IsTraversingGate)
            {
                return true;
            }

            EnemyRegimentAI regimentAi = troop.GetComponent<EnemyRegimentAI>();
            if (regimentAi != null && regimentAi.IsExitingGate)
            {
                return true;
            }
        }

        return false;
    }
}
