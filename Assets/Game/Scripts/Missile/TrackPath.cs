using System.Collections.Generic;
using UnityEngine;

public class TrackPath : MonoBehaviour
{
    [Tooltip("Drag in order from start of level to end. Include checkpoints too if you like — order matters, distance between them doesn't have to be even.")]
    [SerializeField] private List<Transform> waypoints = new();

    [Tooltip("How many indices ahead/behind of a known position to search when re-locating progress. Keeps spirals from confusing it.")]
    [SerializeField] private int searchWindow = 6;

    public int WaypointCount => waypoints.Count;
    public Vector3 GetWaypoint(int i) => waypoints[Mathf.Clamp(i, 0, waypoints.Count - 1)].position;

    /// Finds the closest point on the path SEGMENT-WISE, but only searches near `hintIndex`
    /// instead of globally — this is what prevents picking a point from the wrong loop.
    public float GetProgress(Vector3 worldPos, float hintIndex)
    {
        int center = Mathf.RoundToInt(hintIndex);
        int lo = Mathf.Max(0, center - searchWindow);
        int hi = Mathf.Min(waypoints.Count - 2, center + searchWindow);

        float bestDist = float.MaxValue;
        float bestProgress = hintIndex;

        for (int i = lo; i <= hi; i++)
        {
            Vector3 a = waypoints[i].position;
            Vector3 b = waypoints[i + 1].position;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / ab.sqrMagnitude)
                : 0f;

            Vector3 proj = a + ab * t;
            float d = (worldPos - proj).sqrMagnitude;

            if (d < bestDist)
            {
                bestDist = d;
                bestProgress = i + t;
            }
        }
        return bestProgress;
    }

    /// Returns a world point `lookahead` waypoints ahead of the given progress.
    public Vector3 GetLookaheadPoint(float progress, float lookahead)
    {
        float target = Mathf.Clamp(progress + lookahead, 0f, waypoints.Count - 1);
        int i0 = Mathf.FloorToInt(target);
        int i1 = Mathf.Min(i0 + 1, waypoints.Count - 1);
        float t = target - i0;
        return Vector3.Lerp(GetWaypoint(i0), GetWaypoint(i1), t);
    }
}