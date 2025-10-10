using UnityEngine;

public class LatteSimCompute : MonoBehaviour
{
    [Header("Debug")]
    public bool autoSplat = false;
    public bool runComputeSelfTest;  // tick once in Play Mode
    public bool runCpuWhiteFill;     // tick once in Play Mode
    int testFillKernel;

    [Tooltip("Ceiling for incoming force (UV/sec).")]
    public float forceMaxUVPerSec = 2.0f;


    [Header("Refs")]
    public MeshRenderer surfaceRenderer;   // SurfaceDisk
    public MeshRenderer debugRenderer;     // optional small quad

    [Header("Compute")]
    public ComputeShader fluid;            // LatteFluid.compute

    [Header("Grid")]
    [Range(128, 2048)] public int resolution = 1024;

    [Header("Sim Params")]
    public float timestep = 1f / 60f;
    [Tooltip("Velocity diffusion. Lower = livelier. 0.0003–0.001 typical.")]
    public float viscosity = 0.0005f;
    [Tooltip("Dye fade per second. 0.1–0.5 typical.")]
    public float dyeDissipation = 0.3f;
    [Tooltip("Pressure iterations per frame (30–80 typical).")]
    [Range(1, 200)] public int pressureIters = 40;
    [Tooltip("Jacobi passes for Velocity diffusion (0–20).")]
    [Range(0, 60)] public int velDiffuseIters = 10;

    [Header("Splat Defaults")]
    public float defaultRadius = 0.06f;           // UV units
    [Range(0, 1)] public float defaultHardness = 0.85f;
    [Range(0, 1)] public float defaultAmount = 0.25f;
    public float splatForceScale = 4.0f;          // scales incoming force

    // internals
    RenderTexture velA, velB, dyeA, dyeB, pA, pB, div;
    int kSplat, kAdvect, kAdvectDye, kDiffuseVel, kDiv, kJacobi, kSubtract, kBoundary;
    int tgx, tgy;

    // IDs
    static readonly int _VelWrite = Shader.PropertyToID("VelWrite");
    static readonly int _VelRead = Shader.PropertyToID("VelRead");
    static readonly int _DyeWrite = Shader.PropertyToID("DyeWrite");
    static readonly int _DyeRead = Shader.PropertyToID("DyeRead");
    static readonly int _PressureWrite = Shader.PropertyToID("PressureWrite");
    static readonly int _PressureRead = Shader.PropertyToID("PressureRead");
    static readonly int _Divergence = Shader.PropertyToID("Divergence");
    static readonly int _GridSize = Shader.PropertyToID("GridSize");
    static readonly int _Texel = Shader.PropertyToID("Texel");
    static readonly int _dt = Shader.PropertyToID("dt");
    static readonly int _viscosity = Shader.PropertyToID("viscosity");
    static readonly int _dyeDiss = Shader.PropertyToID("dyeDissipation");
    static readonly int _splatUV = Shader.PropertyToID("splatUV");
    static readonly int _splatRadius = Shader.PropertyToID("splatRadius");
    static readonly int _splatHardness = Shader.PropertyToID("splatHardness");
    static readonly int _splatAmount = Shader.PropertyToID("splatAmount");
    static readonly int _splatForce = Shader.PropertyToID("splatForce");
    static readonly int _Vel0 = Shader.PropertyToID("Vel0");
    static readonly int _jacobiAlpha = Shader.PropertyToID("jacobiAlpha");
    static readonly int _jacobiBeta = Shader.PropertyToID("jacobiBeta");

    void Awake()
    {
        Allocate();
        CacheKernels();

        Debug.Log("LatteSimCompute.Awake()");
        Debug.Log("supportsComputeShaders=" + SystemInfo.supportsComputeShaders);

        if (!fluid) Debug.LogError("LatteSimCompute: Fluid (ComputeShader) is NOT assigned!");

        void CK(int id, string n) { if (id < 0) Debug.LogError("Kernel not found: " + n); else Debug.Log("Kernel OK: " + n + " = " + id); }
        CK(kSplat, "Splat"); CK(kAdvect, "Advect"); CK(kAdvectDye, "AdvectDye");
        CK(kDiffuseVel, "DiffuseVel"); CK(kDiv, "ComputeDiv");
        CK(kJacobi, "JacobiPressure"); CK(kSubtract, "SubtractGrad"); CK(kBoundary, "Boundary");

        // Test kernel
        testFillKernel = fluid.FindKernel("TestFill");
        CK(testFillKernel, "TestFill");



        BindGlobals();
        BindSurface();
    }

    void OnDestroy() => ReleaseAll();

    void Update()
    {
        if (runComputeSelfTest)
        {
            runComputeSelfTest = false;
            Debug.Log("Dispatching TestFill...");
            BindGlobals();
            fluid.SetTexture(testFillKernel, "DyeWrite", dyeA);
            int gx = Mathf.CeilToInt(resolution / 8f);
            int gy = Mathf.CeilToInt(resolution / 8f);
            fluid.Dispatch(testFillKernel, gx, gy, 1);
            BindSurface();
        }

        if (runCpuWhiteFill)
        {
            runCpuWhiteFill = false;
            Debug.Log("CPU white fill...");
            var prev = RenderTexture.active;
            RenderTexture.active = dyeA;
            GL.Clear(false, true, Color.white);
            RenderTexture.active = prev;
            BindSurface();
        }



        Step(timestep);
        if (debugRenderer) debugRenderer.material.mainTexture = dyeA;
    }

    // ---------------- public API ----------------

    public void InjectUV(Vector2 uv, float radius, float hardness, float amount)
    {
        Splat(uv,
              radius <= 0 ? defaultRadius : radius,
              hardness <= 0 ? defaultHardness : hardness,
              amount <= 0 ? defaultAmount : amount,
              Vector2.zero);
    }

    public void InjectUV(Vector2 uv, float radius, float hardness, float amount, Vector2 flowDirUV)
    {
        // clamp force
        if (flowDirUV.sqrMagnitude > forceMaxUVPerSec * forceMaxUVPerSec)
            flowDirUV = flowDirUV.normalized * forceMaxUVPerSec;

        Splat(uv,
              radius <= 0 ? defaultRadius : radius,
              hardness <= 0 ? defaultHardness : hardness,
              amount <= 0 ? defaultAmount : amount,
              flowDirUV * splatForceScale);
    }

    public void Clear()
    {
        ClearRT(velA); ClearRT(velB);
        ClearRT(dyeA); ClearRT(dyeB);
        ClearRT(pA); ClearRT(pB);
        ClearRT(div);
        BindSurface();
    }

    // ---------------- core loop ----------------

    void Step(float dtSec)
    {
        int W = resolution, H = resolution;

        BindGlobals();

        // Optional: diffuse velocity for thickness
        if (velDiffuseIters > 0)
        {
            fluid.SetTexture(kDiffuseVel, "Vel0", velA);
            for (int i = 0; i < velDiffuseIters; i++)
            {
                fluid.SetTexture(kDiffuseVel, "VelRead", velA);
                fluid.SetTexture(kDiffuseVel, "VelWrite", velB);
                Dispatch(kDiffuseVel, W, H); Swap(ref velA, ref velB);
            }
        }

        // Project (divergence free)
        fluid.SetTexture(kDiv, "VelRead", velA);
        fluid.SetTexture(kDiv, "Divergence", div);
        Dispatch(kDiv, W, H);

        // Jacobi on pressure
        fluid.SetFloat(_jacobiAlpha, -1.0f);
        fluid.SetFloat(_jacobiBeta, 4.0f);
        ClearRT(pA); ClearRT(pB);
        for (int i = 0; i < pressureIters; i++)
        {
            fluid.SetTexture(kJacobi, "PressureRead", pA);
            fluid.SetTexture(kJacobi, "PressureWrite", pB);
            fluid.SetTexture(kJacobi, "Divergence", div);
            Dispatch(kJacobi, W, H); Swap(ref pA, ref pB);
        }

        // Subtract gradient
        fluid.SetTexture(kSubtract, "VelRead", velA);
        fluid.SetTexture(kSubtract, "VelWrite", velB);
        fluid.SetTexture(kSubtract, "PressureRead", pA);
        Dispatch(kSubtract, W, H); Swap(ref velA, ref velB);

        // Advect velocity (semi-Lagrangian)
        fluid.SetTexture(kAdvect, "VelRead", velA);
        fluid.SetTexture(kAdvect, "VelWrite", velB);
        Dispatch(kAdvect, W, H); Swap(ref velA, ref velB);

        // Boundaries (simple zero border)
        fluid.SetTexture(kBoundary, "VelWrite", velA);
        fluid.SetTexture(kBoundary, "PressureWrite", pA);
        Dispatch(kBoundary, W, H);

        // Advect dye by velocity
        fluid.SetTexture(kAdvectDye, "VelRead", velA);
        fluid.SetTexture(kAdvectDye, "DyeRead", dyeA);
        fluid.SetTexture(kAdvectDye, "DyeWrite", dyeB);
        Dispatch(kAdvectDye, W, H); Swap(ref dyeA, ref dyeB);

        BindSurface();
    }

    void Splat(Vector2 uv, float radius, float hardness, float amount, Vector2 force)
    {
        int W = resolution, H = resolution;
        BindGlobals();

        fluid.SetVector(_splatUV, new Vector4(uv.x, uv.y, 0, 0));
        fluid.SetFloat(_splatRadius, Mathf.Max(0.0005f, radius));
        fluid.SetFloat(_splatHardness, Mathf.Clamp01(hardness));
        fluid.SetFloat(_splatAmount, Mathf.Clamp01(amount));
        fluid.SetVector(_splatForce, new Vector4(force.x, force.y, 0, 0));

        fluid.SetTexture(kSplat, "VelRead", velA);
        fluid.SetTexture(kSplat, "VelWrite", velB);
        fluid.SetTexture(kSplat, "DyeRead", dyeA);
        fluid.SetTexture(kSplat, "DyeWrite", dyeB);
        Dispatch(kSplat, W, H); Swap(ref velA, ref velB); Swap(ref dyeA, ref dyeB);

        BindSurface();
    }

    // ---------------- setup / utils ----------------

    void Allocate()
    {
        velA = MakeRT(RenderTextureFormat.RGFloat);
        velB = MakeRT(RenderTextureFormat.RGFloat);

        // Dye: use ARGBHalf to guarantee UAV support on DX11
        dyeA = MakeRT(RenderTextureFormat.ARGBHalf);
        dyeB = MakeRT(RenderTextureFormat.ARGBHalf);

        pA = MakeRT(RenderTextureFormat.RFloat);
        pB = MakeRT(RenderTextureFormat.RFloat);
        div = MakeRT(RenderTextureFormat.RFloat);

        Clear();

        // Useful one-time diagnostics
        Debug.Log("supportsComputeShaders=" + SystemInfo.supportsComputeShaders);
        Debug.Log("RW ARGB32=" + SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGB32)
                + " ARGBHalf=" + SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                + " RGFloat=" + SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RGFloat)
                + " RFloat=" + SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat));
        Debug.Log($"dyeA created: format={dyeA.format} randomWrite={dyeA.enableRandomWrite}");
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
        kSplat = fluid.FindKernel("Splat");
        kAdvect = fluid.FindKernel("Advect");
        kAdvectDye = fluid.FindKernel("AdvectDye");
        kDiffuseVel = fluid.FindKernel("DiffuseVel");
        kDiv = fluid.FindKernel("ComputeDiv");
        kJacobi = fluid.FindKernel("JacobiPressure");
        kSubtract = fluid.FindKernel("SubtractGrad");
        kBoundary = fluid.FindKernel("Boundary");

        tgx = Mathf.CeilToInt(resolution / 8f);
        tgy = Mathf.CeilToInt(resolution / 8f);
    }

    void BindGlobals()
    {
        fluid.SetVector(_GridSize, new Vector2(resolution, resolution));
        fluid.SetFloat(_Texel, 1f / resolution);
        fluid.SetFloat(_dt, Mathf.Max(1e-4f, timestep));
        fluid.SetFloat(_viscosity, viscosity);
        fluid.SetFloat(_dyeDiss, dyeDissipation);
    }

    void Dispatch(int kernel, int W, int H) => fluid.Dispatch(kernel, tgx, tgy, 1);

    void Swap(ref RenderTexture a, ref RenderTexture b) { var t = a; a = b; b = t; }

    void ClearRT(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    MaterialPropertyBlock _mpb;
    void ApplyTexture(Renderer r, Texture t)
    {
        if (!r) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetTexture("_MainTex", t);
        _mpb.SetTexture("_BaseMap", t);
        r.SetPropertyBlock(_mpb);
    }
    void BindSurface()
    {
        ApplyTexture(surfaceRenderer, dyeA);
        ApplyTexture(debugRenderer, dyeA);
    }


    void ReleaseAll()
    {
        foreach (var rt in new[] { velA, velB, dyeA, dyeB, pA, pB, div })
            if (rt) rt.Release();
    }
}