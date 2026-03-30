// Assets/Scripts/VesselData.cs
using System;

[Serializable]
public class Vessel
{
    // These fields mirror the compact topology contract exported for Unity.
    public string label;
    public int sn;
    public int tn;
    public float L;
    public float E;
    public int M;
    public float Rp;
    public float Rd;
    public int gamma_profile;
    public float Pext;
}

[Serializable]
public class VesselArray
{
    // JsonUtility needs a wrapper root object rather than a bare array.
    public Vessel[] vessels;
}
