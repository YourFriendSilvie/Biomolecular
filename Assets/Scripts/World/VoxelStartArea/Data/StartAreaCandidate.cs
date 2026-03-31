using UnityEngine;

internal struct StartAreaCandidate
{
    public Vector3 center;
    public Vector3 surfaceNormal;
    public Vector3 nearestFreshwaterPoint;
    public float freshwaterDistanceMeters;
    public float score;
}
