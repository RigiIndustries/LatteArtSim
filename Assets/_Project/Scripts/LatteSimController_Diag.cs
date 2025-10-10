using UnityEngine;

/// <summary>
/// Diagnose-Controller für die Latte-RT-Pipeline:
/// - Erstellt A/B-RTs
/// - Bindet READ-RT auf die SurfaceDisk (URP/Unlit: _BaseMap/_MainTex)
/// - Bietet Sichttests per Tastatur (1..7)
/// </summary>
public class LatteSimController_Diag : MonoBehaviour
{
    [Header("Refs")]
    public MeshRenderer surfaceRenderer;   // Renderer der SurfaceDisk
    public MeshRenderer debugRenderer;     // optionales Quad vor der Kamera
    public Material injectMat;             // Hidden/LatteInject (optional)
    public Material blurMat;               // Hidden/LatteBlur   (optional)

    [Header("RT")]
    [Range(128, 2048)] public int resolution = 512;

    RenderTexture rtA, rtB;    // Ping-Pong
    RenderTexture read, write; // aktuell lesen/schreiben

    // Shader IDs
    static readonly int _BaseMap   = Shader.PropertyToID("_BaseMap");
    static readonly int _MainTex   = Shader.PropertyToID("_MainTex");
    static readonly int _Direction = Shader.PropertyToID("_Direction");
    static readonly int _Center    = Shader.PropertyToID("_Center");
    static readonly int _Radius    = Shader.PropertyToID("_Radius");

    // ------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------
    void Awake()
    {
        CreateRTs();
        ClearRT(read, Color.black);
        HardBind("Awake");

        if (debugRenderer) debugRenderer.material.SetTexture(_MainTex, read);

        // Diagnoseausgabe zum Material/Properties
        if (surfaceRenderer)
        {
            var m = surfaceRenderer.material;
            Debug.Log($"[Diag] Surface shader={m.shader.name}  _BaseMap?={m.HasProperty(_BaseMap)}  _MainTex?={m.HasProperty(_MainTex)}");
        }
        else
        {
            Debug.LogWarning("[Diag] surfaceRenderer is NULL");
        }

        LogRTs("Awake");
    }

    void OnDestroy()
    {
        if (rtA) rtA.Release();
        if (rtB) rtB.Release();
    }

    // ------------------------------------------------------
    // RT / Bind / Utils
    // ------------------------------------------------------
    void CreateRTs()
    {
        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGB32, 0)
        {
            msaaSamples = 1,
            sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear)
        };

        rtA = new RenderTexture(desc) { name = "LatteSim_A" };
        rtB = new RenderTexture(desc) { name = "LatteSim_B" };
        rtA.Create(); rtB.Create();

        read  = rtA;
        write = rtB;
    }

    void ClearRT(RenderTexture rt, Color c)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, c);
        RenderTexture.active = prev;
    }

    void Swap()
    {
        var tmp = read; read = write; write = tmp;
        HardBind("Swap");
        LogRTs("After Swap");
        if (debugRenderer) debugRenderer.material.SetTexture(_MainTex, read);
    }

    void HardBind(string from)
    {
        if (!surfaceRenderer) return;

        var m = surfaceRenderer.material;    // instanziert!
        m.SetTexture(_BaseMap, read);        // URP/Unlit BaseMap
        m.SetTexture(_MainTex, read);        // Fallback

        // Sichtbarer Name der aktuell gebundenen Textur
        var t = m.GetTexture(_BaseMap);
        if (!t) t = m.GetTexture(_MainTex);
        Debug.Log($"[Diag] {from}: Bound READ = {(t ? t.name : "<null>")} to SurfaceDisk");
    }

    void LogRTs(string from)
    {
        string rn = read  ? read.name  : "<null>";
        string wn = write ? write.name : "<null>";
        int    ri = read  ? read.GetInstanceID()  : -1;
        int    wi = write ? write.GetInstanceID() : -1;
        int    ai = rtA   ? rtA.GetInstanceID()   : -1;
        int    bi = rtB   ? rtB.GetInstanceID()   : -1;
        Debug.Log($"[Diag] {from}: read={rn}/{ri}, write={wn}/{wi}  rtA={ai} rtB={bi}");
    }

    // ------------------------------------------------------
    // Sichttests
    // ------------------------------------------------------
    void Fill(Color c)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = read;
        GL.Clear(false, true, c);
        RenderTexture.active = prev;
        HardBind("Fill");
    }

    Texture2D _checker;
    Texture2D GetChecker()
    {
        if (_checker) return _checker;

        _checker = new Texture2D(64, 64, TextureFormat.RGBA32, false, false) { name = "CheckerCPU" };
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            bool on = ((x / 8 + y / 8) % 2) == 0;
            _checker.SetPixel(x, y, on ? Color.white : Color.black);
        }
        _checker.Apply(false);
        return _checker;
    }

    // ------------------------------------------------------
    // Update: Key-Shortcuts
    // ------------------------------------------------------
    void Update()
    {
        // 1 = read schwarz, 2 = read weiß -> direkt sichtbar
        if (Input.GetKeyDown(KeyCode.Alpha1)) Fill(Color.black);
        if (Input.GetKeyDown(KeyCode.Alpha2)) Fill(Color.white);

        // 3 = Checker direkt ins Material (bypasst RT/Blit komplett)
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            if (surfaceRenderer)
            {
                var m = surfaceRenderer.material;
                var chk = GetChecker();
                m.SetTexture(_BaseMap, chk);
                m.SetTexture(_MainTex, chk);
                Debug.Log("[Diag] Set CHECKER directly on material");
            }
        }

        // 4 = Inject-Test (LatteInject, Pass 0)
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            if (!injectMat) { Debug.LogError("[Diag] injectMat missing"); return; }

            // sinnvolle Defaults
            var center = new Vector2(0.5f, 0.5f);
            float amount = 0.9f;
            float radius = 0.08f;     // ~8% der Breite
            float hardness = 0.6f;

            // -> Shader-Parameter
            injectMat.SetVector("_Center", new Vector4(center.x, center.y, 0, 0));
            injectMat.SetFloat("_Amount", Mathf.Clamp01(amount));
            injectMat.SetFloat("_Radius", Mathf.Max(0.0001f, radius));
            injectMat.SetFloat("_Hardness", Mathf.Clamp01(hardness));

            // Blit + Swap
            Graphics.Blit(read, write, injectMat, 0);
            Swap();
            Debug.Log("[Diag] Inject pass 0");
        }


        // 5 = einfacher Blur (H + V)
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            if (!blurMat) { Debug.LogWarning("[Diag] blurMat missing"); return; }

            blurMat.SetVector(_Direction, new Vector4(1, 0, 0, 0));
            Graphics.Blit(read, write, blurMat, 0);
            Swap();

            blurMat.SetVector(_Direction, new Vector4(0, 1, 0, 0));
            Graphics.Blit(read, write, blurMat, 0);
            Swap();

            Debug.Log("[Diag] Blur HV");
        }

        // 6 = Swap ohne Blit
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            Debug.Log("[Diag] Manual Swap");
            Swap();
        }

        // 7 = Schreibe GRAU in write, dann Swap -> read sollte GRAU anzeigen
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            Debug.Log("[Diag] Fill WRITE then Swap");
            var prev = RenderTexture.active;
            RenderTexture.active = write;
            GL.Clear(false, true, new Color(0.5f, 0.5f, 0.5f, 1));
            RenderTexture.active = prev;
            Swap();
        }

        // Optional: falls anderer Code das Material überschreibt
        // HardBind("Tick");
    }
}
