using System.Collections.Generic;
using UnityEngine;

internal sealed class GeneratedRiver
{
    public readonly List<Vector3> points = new List<Vector3>();
    public readonly List<float> widths = new List<float>();
    public readonly List<Vector3> waterPath = new List<Vector3>();
    public readonly List<float> widthProfile = new List<float>();
    public float baseWidth;
    public Bounds influenceBounds;
    public GameObject waterObject;
}
