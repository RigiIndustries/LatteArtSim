using UnityEngine;

/// <summary>
/// Main Latte-Art-Sim controller. Manages Lattefluid shader.
/// GPU-based 2D fluid + dye simulation for latte surface.
/// Manages render textures (ping-pong) and dispatches compute kernels each frame.
/// </summary>
public class LatteSimCompute : MonoBehaviour
{
    [Header("State")]
    [Tooltip("Master toggle for simulation step.")]
    public bool simEnabled = true;

    [Header("Compute")]
    [Tooltip("Compute shader implementing the fluid + dye kernels.")]
    public ComputeShader fluid;

    [Header("Refs")]
    [Tooltip("Renderer of the cup surface mesh; receives dye texture.")]
    public MeshRenderer surfaceRenderer;

    [Tooltip("Optional debug quad/mesh to preview the dye texture.")]
    public MeshRenderer debugRenderer;

    [Header("Grid")]
    [Tooltip("Simulation resolution (square). Higher = sharper but more expensive.")]
    [Range(128, 2048)] public int resolution = 1024;

    [Header("Sim Params")]
    [Tooltip("Fixed simulation timestep (seconds).")]
    public float timestep = 1f / 60f;

    [Tooltip("Velocity diffusion coefficient. Lower = livelier, higher = more viscous.")]
    public float viscosity = 0.0005f;

    [Tooltip("Dye fade per second. Higher = pattern disappears faster.")]
    public float dyeDissipation = 0.3f;

    [Tooltip("Pressure solver iterations. Higher = less compressible, more stable (slower).")]
    [Range(1, 200)] public int pressureIters = 40;

    [Tooltip("Velocity diffusion iterations (not used in the current Step path, kept for future).")]
    [Range(0, 60)] public int velDiffuseIters = 10;

    [Header("Splat Defaults")]
    [Tooltip("Default splat radius in UV units.")]
    public float defaultRadius = 0.06f;

    [Range(0, 1)] public float defaultHardness = 0.85f;
    [Range(0, 1)] public float defaultAmount = 0.25f;

    [Tooltip("Scales incoming flowDir into velocity impulse.")]
    public float splatForceScale = 4.0f;

    [Header("Input Limits")]
    [Tooltip("Clamp incoming force magnitude (UV/sec). Prevents unstable impulses.")]
    public float forceMaxUVPerSec = 2.0f;

    // Ping-pong simulation targets
    RenderTexture velA, velB;
    RenderTexture dyeA, dyeB;
    RenderTexture pA, pB;
    RenderTexture div;

    // Cached kernel indices
    int kSplat, kAdvect, kAdvectDye, kDiv, kJacobi, kSubtract;
    int kDiffuseVel, kBoundary; // cached but not used in current Step

    // Dispatch group sizes (based on compute THREADS = 8)
    int tgx, tgy;

    // Shader property IDs (avoid string lookups every frame)
    static readonly int ID_GridSize      = Shader.PropertyToID("GridSize");
    static readonly int ID_Texel         = Shader.PropertyToID("Texel");
    static readonly int ID_dt            = Shader.PropertyToID("dt");
    static readonly int ID_viscosity     = Shader.PropertyToID("viscosity");
    static readonly int ID_dyeDiss       = Shader.PropertyToID("dyeDissipation");

    static readonly int ID_splatUV       = Shader.PropertyToID("splatUV");
    static readonly int ID_splatRadius   = Shader.PropertyToID("splatRadius");
    static readonly int ID_splatHardness = Shader.PropertyToID("splatHardness");
    static readonly int ID_splatAmount   = Shader.PropertyToID("splatAmount");
    static readonly int ID_splatForce    = Shader.PropertyToID("splatForce");

    MaterialPropertyBlock _mpb;

    void Awake()
    {
        // Hard fail early if compute shader is missing: avoids null spam and partial state.
        if (!fluid)
        {
            enabled = false;
            return;
        }

        Allocate();
        CacheKernels();
        BindSurface();
    }

    void OnDestroy()
    {
        ReleaseAll();
    }

    void Update()
    {
        if (!simEnabled || !fluid)
            return;

        Step(timestep);

        // Optional: easy runtime preview on a debug quad without touching MPBs.
        if (debugRenderer)
            debugRenderer.material.mainTexture = dyeA;
    }

    // ---------------- Public API ----------------

    /// <summary>
    /// Injects dye only (no velocity impulse).
    /// </summary>
    public void InjectUV(Vector2 uv, float radius, float hardness, float amount)
    {
        Splat(
            uv,
            radius <= 0 ? defaultRadius : radius,
            hardness <= 0 ? defaultHardness : hardness,
            amount <= 0 ? defaultAmount : amount,
            Vector2.zero
        );
    }

    /// <summary>
    /// Injects dye and velocity impulse (flowDirUV in UV/sec).
    /// </summary>
    public void InjectUV(Vector2 uv, float radius, float hardness, float amount, Vector2 flowDirUV)
    {
        flowDirUV = ClampForce(flowDirUV);

        Splat(
            uv,
            radius <= 0 ? defaultRadius : radius,
            hardness <= 0 ? defaultHardness : hardness,
            amount <= 0 ? defaultAmount : amount,
            flowDirUV * splatForceScale
        );
    }

    /// <summary>
    /// Clears all simulation buffers.
    /// </summary>
    public void Clear()
    {
        ClearRT(velA); ClearRT(velB);
        ClearRT(dyeA); ClearRT(dyeB);
        ClearRT(pA);   ClearRT(pB);
        ClearRT(div);

        BindSurface();
    }

    // ---------------- Simulation ----------------

    /// <summary>
    /// One simulation step:
    /// 1) divergence
    /// 2) pressure solve (Jacobi)
    /// 3) projection (subtract gradient)
    /// 4) advect velocity
    /// 5) advect dye
    /// </summary>
    void Step(float dtSec)
    {
        BindGlobals();

        // 1) Divergence
        fluid.SetTexture(kDiv, "VelRead", velA);
        fluid.SetTexture(kDiv, "Divergence", div);
        Dispatch(kDiv);

        // 2) Pressure solve
        if (pressureIters > 0)
        {
            ClearRT(pA);
            ClearRT(pB);

            for (int i = 0; i < pressureIters; i++)
            {
                fluid.SetTexture(kJacobi, "PressureRead", pA);
                fluid.SetTexture(kJacobi, "PressureWrite", pB);
                fluid.SetTexture(kJacobi, "Divergence", div);
                Dispatch(kJacobi);
                Swap(ref pA, ref pB);
            }

            // 3) Projection
            fluid.SetTexture(kSubtract, "VelRead", velA);
            fluid.SetTexture(kSubtract, "VelWrite", velB);
            fluid.SetTexture(kSubtract, "PressureRead", pA);
            Dispatch(kSubtract);
            Swap(ref velA, ref velB);
        }

        // 4) Advect velocity
        fluid.SetTexture(kAdvect, "VelRead", velA);
        fluid.SetTexture(kAdvect, "VelWrite", velB);
        Dispatch(kAdvect);
        Swap(ref velA, ref velB);

        // 5) Advect dye
        fluid.SetTexture(kAdvectDye, "VelRead", velA);
        fluid.SetTexture(kAdvectDye, "DyeRead", dyeA);
        fluid.SetTexture(kAdvectDye, "DyeWrite", dyeB);
        Dispatch(kAdvectDye);
        Swap(ref dyeA, ref dyeB);

        BindSurface();
    }

    /// <summary>
    /// Writes into dye + velocity via the compute "Splat" kernel and ping-pongs buffers.
    /// </summary>
    void Splat(Vector2 uv, float radius, float hardness, float amount, Vector2 force)
    {
        BindGlobals();

        fluid.SetVector(ID_splatUV, new Vector4(uv.x, uv.y, 0f, 0f));
        fluid.SetFloat(ID_splatRadius, Mathf.Max(0.0005f, radius));
        fluid.SetFloat(ID_splatHardness, Mathf.Clamp01(hardness));
        fluid.SetFloat(ID_splatAmount, Mathf.Clamp01(amount));
        fluid.SetVector(ID_splatForce, new Vector4(force.x, force.y, 0f, 0f));

        fluid.SetTexture(kSplat, "VelRead", velA);
        fluid.SetTexture(kSplat, "VelWrite", velB);
        fluid.SetTexture(kSplat, "DyeRead", dyeA);
        fluid.SetTexture(kSplat, "DyeWrite", dyeB);

        Dispatch(kSplat);

        Swap(ref velA, ref velB);
        Swap(ref dyeA, ref dyeB);

        BindSurface();
    }

    // ---------------- Setup / Utilities ----------------

    /// <summary>
    /// Prevents large impulses from destabilizing the sim.
    /// </summary>
    Vector2 ClampForce(Vector2 v)
    {
        float maxSqr = forceMaxUVPerSec * forceMaxUVPerSec;
        if (v.sqrMagnitude > maxSqr)
            return v.normalized * forceMaxUVPerSec;
        return v;
    }

    void Allocate()
    {
        velA = MakeRT(RenderTextureFormat.RGFloat);
        velB = MakeRT(RenderTextureFormat.RGFloat);

        dyeA = MakeRT(RenderTextureFormat.ARGBHalf);
        dyeB = MakeRT(RenderTextureFormat.ARGBHalf);

        pA  = MakeRT(RenderTextureFormat.RFloat);
        pB  = MakeRT(RenderTextureFormat.RFloat);

        div = MakeRT(RenderTextureFormat.RFloat);

        Clear();
    }

    RenderTexture MakeRT(RenderTextureFormat fmt)
    {
        var rt = new RenderTexture(resolution, resolution, 0, fmt)
        {
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();
        return rt;
    }

    void CacheKernels()
    {
        kSplat      = fluid.FindKernel("Splat");
        kAdvect     = fluid.FindKernel("Advect");
        kAdvectDye  = fluid.FindKernel("AdvectDye");
        kDiffuseVel = fluid.FindKernel("DiffuseVel");
        kDiv        = fluid.FindKernel("ComputeDiv");
        kJacobi     = fluid.FindKernel("JacobiPressure");
        kSubtract   = fluid.FindKernel("SubtractGrad");
        kBoundary   = fluid.FindKernel("Boundary");

        tgx = Mathf.CeilToInt(resolution / 8f);
        tgy = Mathf.CeilToInt(resolution / 8f);
    }

    /// <summary>
    /// Parameters shared by most kernels (set once per frame/dispatch).
    /// </summary>
    void BindGlobals()
    {
        fluid.SetVector(ID_GridSize, new Vector2(resolution, resolution));
        fluid.SetFloat(ID_Texel, 1f / resolution);
        fluid.SetFloat(ID_dt, Mathf.Max(1e-4f, timestep));
        fluid.SetFloat(ID_viscosity, viscosity);
        fluid.SetFloat(ID_dyeDiss, dyeDissipation);
    }

    void Dispatch(int kernel)
    {
        fluid.Dispatch(kernel, tgx, tgy, 1);
    }

    static void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        var t = a; a = b; b = t;
    }

    static void ClearRT(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    /// <summary>
    /// Applies the current dye texture to the visible surface (via MPB, no material instancing).
    /// </summary>
    void BindSurface()
    {
        ApplyTexture(surfaceRenderer, dyeA);
        ApplyTexture(debugRenderer, dyeA);
    }

    void ApplyTexture(Renderer r, Texture t)
    {
        if (!r) return;

        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetTexture("_MainTex", t);
        _mpb.SetTexture("_BaseMap", t);
        r.SetPropertyBlock(_mpb);
    }

    void ReleaseAll()
    {
        ReleaseRT(velA); ReleaseRT(velB);
        ReleaseRT(dyeA); ReleaseRT(dyeB);
        ReleaseRT(pA);   ReleaseRT(pB);
        ReleaseRT(div);
    }

    static void ReleaseRT(RenderTexture rt)
    {
        if (rt) rt.Release();
    }
}
