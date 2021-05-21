using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// source https://github.com/QianMo/Unity-Mirror-Reflection-Example/blob/master/Assets/MirrorReflection/MirrorReflection.cs

[ExecuteInEditMode]
public class RipplesRenderer : MonoBehaviour
{
    [SerializeField] private LayerMask m_rippleLayers = -1;

    private Hashtable m_rippleCameras = new Hashtable(); // Camera -> Camera table 

    private RenderTexture m_ripplesTexture = null;
    private Vector2Int m_OldRippleTextureSize = Vector2Int.zero;

    private static bool s_InsideRendering = false;

    [SerializeField] private Renderer m_renderer;

    private void OnValidate()
    {
        if (!m_renderer)
        {
            m_renderer = GetComponent<Renderer>();
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRender;
    }

    // This is called when it's known that the object will be rendered by some
    // camera. We render reflections and do other updates here.
    // Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void BeginCameraRender(ScriptableRenderContext context, Camera cam)
    {
        if (!enabled || !m_renderer || !m_renderer.sharedMaterial || !m_renderer.enabled)
            return;

        //Camera cam = Camera.current;
        //if (!cam)
        //    return;

        // Safeguard from recursive reflections.
        if (s_InsideRendering)
            return;

        s_InsideRendering = true;

        Camera ripplesCamera;
        CreateRippleObjects(cam, out ripplesCamera);

        // find out the reflection plane: position and normal in world space
        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        UpdateCameraModes(cam, ripplesCamera);

        ripplesCamera.worldToCameraMatrix = cam.worldToCameraMatrix;

        ripplesCamera.projectionMatrix = cam.projectionMatrix;

        ripplesCamera.targetTexture = m_ripplesTexture;
        ripplesCamera.transform.position = cam.transform.position;
        ripplesCamera.transform.rotation = cam.transform.rotation;
        ripplesCamera.ResetWorldToCameraMatrix();
        UniversalRenderPipeline.RenderSingleCamera(context, ripplesCamera);
        Material[] materials = m_renderer.sharedMaterials;
        foreach (Material mat in materials)
        {
            if (mat.HasProperty("_HeightTex"))
                mat.SetTexture("_HeightTex", m_ripplesTexture);
        }

        s_InsideRendering = false;
    }


    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        if (m_ripplesTexture)
        {
            DestroyImmediate(m_ripplesTexture);
            m_ripplesTexture = null;
        }
        foreach (DictionaryEntry kvp in m_rippleCameras)
            DestroyImmediate(((Camera)kvp.Value).gameObject);
        m_rippleCameras.Clear();

        RenderPipelineManager.beginCameraRendering -= BeginCameraRender;
    }


    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
            return;
        // set camera to clear the same way as current camera
        dest.clearFlags = CameraClearFlags.Color;
        dest.backgroundColor = Color.black;
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
        dest.cullingMask = m_rippleLayers;
    }

    // On-demand create any objects we need
    private void CreateRippleObjects(Camera currentCamera, out Camera reflectionCamera)
    {
        reflectionCamera = null;

        // Reflection render texture
        if (!m_ripplesTexture || m_OldRippleTextureSize.x != Screen.currentResolution.width || m_OldRippleTextureSize.y != Screen.currentResolution.height)
        {
            if (m_ripplesTexture)
                DestroyImmediate(m_ripplesTexture);
            m_ripplesTexture = new RenderTexture(Screen.currentResolution.width, Screen.currentResolution.height, 16);
            m_ripplesTexture.useMipMap = true;
            m_ripplesTexture.autoGenerateMips = true;
            m_ripplesTexture.name = "__RipplesTexture" + GetInstanceID();
            m_ripplesTexture.isPowerOfTwo = true;
            m_ripplesTexture.hideFlags = HideFlags.DontSave;
            m_OldRippleTextureSize = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
        }

        // |Camera for reflection
        reflectionCamera = m_rippleCameras[currentCamera] as Camera;
        if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject("Ripples Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera));
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.transform.position = transform.position;
            reflectionCamera.transform.rotation = transform.rotation;
            go.hideFlags = HideFlags.HideAndDontSave;
            m_rippleCameras[currentCamera] = reflectionCamera;
        }
    }
}