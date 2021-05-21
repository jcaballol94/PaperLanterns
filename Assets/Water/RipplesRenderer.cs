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
    private int m_OldRippleTextureSize = 0;

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
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }

    // On-demand create any objects we need
    private void CreateRippleObjects(Camera currentCamera, out Camera reflectionCamera)
    {
        reflectionCamera = null;

        // Reflection render texture
        if (!m_ripplesTexture || m_OldRippleTextureSize != Screen.currentResolution.height)
        {
            if (m_ripplesTexture)
                DestroyImmediate(m_ripplesTexture);
            m_ripplesTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16);
            m_ripplesTexture.useMipMap = true;
            m_ripplesTexture.autoGenerateMips = true;
            m_ripplesTexture.name = "__MirrorReflection" + GetInstanceID();
            m_ripplesTexture.isPowerOfTwo = true;
            m_ripplesTexture.hideFlags = HideFlags.DontSave;
            m_OldRippleTextureSize = m_TextureSize;
        }

        // |Camera for reflection
        reflectionCamera = m_rippleCameras[currentCamera] as Camera;
        if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject("Mirror Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera));
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.transform.position = transform.position;
            reflectionCamera.transform.rotation = transform.rotation;
            go.hideFlags = HideFlags.HideAndDontSave;
            m_rippleCameras[currentCamera] = reflectionCamera;
        }
    }

    // 0 or 1（Extended sign: returns -1, 0 or 1 based on sign of a）
    private static float sgn(float a)
    {
        if (a > 0.0f) return 1.0f;
        if (a < 0.0f) return -1.0f;
        return 0.0f;
    }

    //（Given position/normal of the plane, calculates plane in camera space.）
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    // Adjusts the given projection matrix so that near plane is the given clipPlane）
    // clipPlane is given in camera space. See article in Game Programming Gems 5.）
    private static void CalculateObliqueMatrix(ref Matrix4x4 projection, Vector4 clipPlane)
    {
        Vector4 q = projection.inverse * new Vector4
        (sgn(clipPlane.x),
            sgn(clipPlane.y),
            1.0f,
            1.0f);

        Vector4 c = clipPlane * (2.0F / (Vector4.Dot(clipPlane, q)));
        // third row = clip plane - fourth row）
        projection[2] = c.x - projection[3];
        projection[6] = c.y - projection[7];
        projection[10] = c.z - projection[11];
        projection[14] = c.w - projection[15];
    }

    // Calculates reflection matrix around the given plane）
    private static void CalculateReflectionMatrix(out Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }
}