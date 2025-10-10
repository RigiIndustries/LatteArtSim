using UnityEngine;

public class LatteSimController : MonoBehaviour
{
    public enum AfterInjectMode { None, Blur, Diffusion }

    [Header("Refs")]
    public MeshRenderer surfaceRenderer;   // cup liquid surface (SurfaceDisk)
    public MeshRenderer debugRenderer;     // optional small quad to preview READ

    [Header("Materials")]
    public Material injectMat;             // Hidden/LatteInject
    public Material blurMat;               // Hidden/LatteBlur (normalized)
    public Material diffusionMat;          // Hidden/LatteDiffusionImplicit (Jacobi)

    [Header("RT")]
    [Range(128, 4096)] public int resolution = 1024;
    public RenderTextureFormat rtFormat = RenderTextureFormat.ARGB32;
    public bool useSRGB = true; // tip: try OFF if you see micro-noise

    [Header("Simulation")]
    public AfterInjectMode afterInject = AfterInjectMode.None;   // what to run immediately after InjectUV
    [Range(0, 4)] public int blurIterations = 1;                 // for Blur mode

    [Tooltip("Implicit diffusion strength (lambda) per iteration. Stable for any value.")]
    [Range(0f, 3f)] public float diffusionLambda = 0.5f;         // typical 0.2 - 1.0
    [Tooltip("Zeroing threshold to kill tiny numerical speckle.")]
    [Range(0f, 0.01f)] public float diffusionZero = 0.0005f;

    [Tooltip("How many diffusion iterations to run after each inject.")]
    [Range(0, 64)] public int diffusionStepsOnInject = 4;

    [Tooltip("Run diffusion automatically each frame (good for continuous softening).Set the iterations per frame below.")]
    public bool runDiffusionEveryFrame = false;

    [Range(0, 64)] public int diffusionIterationsPerFrame = 0;   // run N Jacobi iterations per Update when enabled

    [Header("Diagnostics")]
    public bool hardBindEachFrame = false;                       // rebind texture every Update while debugging
    public bool enableHotkeys = true;                            // 1..7, D (+10, +50), R

    // --- internals ---
    RenderTexture rtA, rtB, read, write;

    static readonly int _BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int _MainTex = Shader.PropertyToID("_MainTex");
    static readonly int _Center = Shader.PropertyToID("_Center");
    static readonly int _Radius = Shader.PropertyToID("_Radius");
    static readonly int _Hardness = Shader.PropertyToID("_Hardness");
    static readonly int _Amount = Shader.PropertyToID("_Amount");
    static readonly int _Direction = Shader.PropertyToID("_Direction");
    static readonly int _Lambda = Shader.PropertyToID("_Lambda");
    static readonly int _Zero = Shader.PropertyToID("_Zero");

    Texture2D _checker;

    // ---------------------------------------------------------------------
    void Awake()
    {
        CreateRTs();
        ClearRT(rtA, Color.black);
        ClearRT(rtB, Color.black);

        read = rtA;
        write = rtB;

        BindReadToSurface("Awake");
        BindReadToDebug("Awake");

        Debug.Log($"[Latte] Awake: READ={read.name} WRITE={write.name} res={resolution}");
    }

    void OnDestroy()
    {
        SafeRelease(ref rtA);
        SafeRelease(ref rtB);
        if (_checker != null) Destroy(_checker);
    }

    void Update()
    {
        if (hardBindEachFrame)
        {
            BindReadToSurface("Tick");
            BindReadToDebug("Tick");
        }

        if (enableHotkeys) HandleHotkeys();

        if (runDiffusionEveryFrame && diffusionMat && diffusionIterationsPerFrame > 0)
        {
            RunDiffusion(diffusionIterationsPerFrame);
        }
    }

    // ---------------------------------------------------------------------
    // Public API used by input scripts
    // ---------------------------------------------------------------------
    /// <summary>
    /// Injects "milk" into the sim at a given UV position.
    /// </summary>
    public void InjectUV(Vector2 uv, float radius = 0.06f, float hardness = 0.5f, float amount = 1f)
    {
        if (!injectMat || read == null || write == null) return;

        injectMat.SetVector(_Center, new Vector4(uv.x, uv.y, 0, 0));
        injectMat.SetFloat(_Radius, Mathf.Max(0.0001f, radius));
        injectMat.SetFloat(_Hardness, Mathf.Clamp01(hardness));
        injectMat.SetFloat(_Amount, Mathf.Clamp01(amount));

        Graphics.Blit(read, write, injectMat, 0);
        Swap();

        switch (afterInject)
        {
            case AfterInjectMode.Blur:
                RunBlur(Mathf.Max(1, blurIterations));
                break;
            case AfterInjectMode.Diffusion:
                if (diffusionStepsOnInject > 0) RunDiffusion(diffusionStepsOnInject);
                break;
        }
    }

    /// <summary>
    /// Normalized separable blur (H then V), optional cosmetic step.
    /// </summary>
    public void RunBlur(int iterations = 1)
    {
        if (!blurMat || read == null || write == null) return;

        for (int i = 0; i < Mathf.Max(1, iterations); i++)
        {
            blurMat.SetVector(_Direction, new Vector2(1, 0)); // horizontal
            Graphics.Blit(read, write, blurMat, 0); Swap();

            blurMat.SetVector(_Direction, new Vector2(0, 1)); // vertical
            Graphics.Blit(read, write, blurMat, 0); Swap();
        }
    }

    /// <summary>
    /// Implicit diffusion (Jacobi):
    /// x_new = (c + lambda * (L+R+U+D)) / (1 + 4*lambda)
    /// Stable even with many iterations; use lambda ~0.2-1.0
    /// </summary>
    public void RunDiffusion(int iterations = 1)
    {
        if (!diffusionMat || read == null || write == null)
        {
            Debug.LogWarning("[Latte] Diffusion skipped: material or RTs missing.");
            return;
        }

        diffusionMat.SetFloat(_Lambda, diffusionLambda);
        diffusionMat.SetFloat(_Zero, diffusionZero);

        for (int i = 0; i < Mathf.Max(1, iterations); i++)
        {
            Graphics.Blit(read, write, diffusionMat, 0);
            Swap();
        }
    }

    // ---------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------
    void CreateRTs()
    {
        SafeRelease(ref rtA);
        SafeRelease(ref rtB);

        var desc = new RenderTextureDescriptor(resolution, resolution, rtFormat, 0)
        {
            msaaSamples = 1,
            sRGB = useSRGB,
            enableRandomWrite = false
        };

        rtA = new RenderTexture(desc) { name = "LatteSim_A" };
        rtB = new RenderTexture(desc) { name = "LatteSim_B" };
        rtA.wrapMode = rtB.wrapMode = TextureWrapMode.Clamp;
        rtA.filterMode = rtB.filterMode = FilterMode.Bilinear;
        rtA.Create();
        rtB.Create();
    }

    void Swap()
    {
        var t = read; read = write; write = t;
        BindReadToSurface("Swap");
        BindReadToDebug("Swap");
    }

    void BindReadToSurface(string from)
    {
        if (!surfaceRenderer || read == null) return;
        var m = surfaceRenderer.material; // instance
        m.SetTexture(_BaseMap, read);     // URP Unlit/Lit
        m.SetTexture(_MainTex, read);     // built-in fallback
    }

    void BindReadToDebug(string from)
    {
        if (!debugRenderer || read == null) return;
        debugRenderer.material.mainTexture = read;
    }

    void ClearRT(RenderTexture rt, Color c)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, c);
        RenderTexture.active = prev;
    }

    void SafeRelease(ref RenderTexture rt)
    {
        if (rt == null) return;
        if (rt.IsCreated()) rt.Release();
        Destroy(rt);
        rt = null;
    }

    // ---------------------------------------------------------------------
    // Diagnostics & utilities
    // ---------------------------------------------------------------------
    void HandleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) Fill(Color.black, "Key[1] Black");
        if (Input.GetKeyDown(KeyCode.Alpha2)) Fill(Color.white, "Key[2] White");
        if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyChecker();
        if (Input.GetKeyDown(KeyCode.Alpha4)) InjectUV(new Vector2(0.5f, 0.5f), 0.08f, 0.6f, 1f);
        if (Input.GetKeyDown(KeyCode.Alpha5)) RunBlur(blurIterations);
        if (Input.GetKeyDown(KeyCode.Alpha6)) Swap();
        if (Input.GetKeyDown(KeyCode.Alpha7)) { FillWrite(Color.gray); Swap(); }
        if (Input.GetKeyDown(KeyCode.D)) RunDiffusion(1);           // +1 step
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.D)) RunDiffusion(10); // +10
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.D)) RunDiffusion(50); // +50 burst
        if (Input.GetKeyDown(KeyCode.R)) ResetSim();
    }

    void Fill(Color c, string label)
    {
        if (read == null) return;
        ClearRT(read, c);
        BindReadToSurface(label);
        BindReadToDebug(label);
    }

    void FillWrite(Color c)
    {
        if (write == null) return;
        ClearRT(write, c);
    }

    void ResetSim()
    {
        ClearRT(rtA, Color.black);
        ClearRT(rtB, Color.black);
        read = rtA;
        write = rtB;
        BindReadToSurface("Reset");
        BindReadToDebug("Reset");
        Debug.Log("[Latte] Reset sim");
    }

    void ApplyChecker()
    {
        if (!surfaceRenderer) return;
        if (_checker == null)
        {
            _checker = GenerateChecker(256, 256, 16);
            _checker.Apply(false, true);
        }
        var m = surfaceRenderer.material;
        m.SetTexture(_BaseMap, _checker);
        m.SetTexture(_MainTex, _checker);
    }

    static Texture2D GenerateChecker(int w, int h, int cell)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool on = ((x / cell) + (y / cell)) % 2 == 0;
                tex.SetPixel(x, y, on ? new Color(0.9f, 0.9f, 0.9f, 1) : new Color(0.1f, 0.1f, 0.1f, 1));
            }
        }
        return tex;
    }
}
