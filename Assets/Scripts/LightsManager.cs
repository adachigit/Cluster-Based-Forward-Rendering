using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class LightsManager : MonoBehaviour
{
    public GameObject m_LightsGroupObject;
    public InputField m_LightsCountInput;
    public Camera m_Camera;

    private float MinZ;
    private float MaxZ;
    private float MinY;
    private float MaxY;
    private float MinX;
    private float MaxX;

    private void Awake()
    {
        Screen.SetResolution(1280, 720, true);
    }

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start()
    {
        MinZ = m_Camera.nearClipPlane;// + (m_Camera.farClipPlane - m_Camera.nearClipPlane) / 5.0f;
        MaxZ = m_Camera.farClipPlane;

        MinY = 0.0f;//m_Camera.nearClipPlane * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        MinX = m_Camera.aspect * MinY;
    }

    public void CreateLights()
    {
        for(int i = 0; i < m_LightsGroupObject.transform.childCount; ++i)
        {
            Destroy(m_LightsGroupObject.transform.GetChild(i).gameObject);
        }

        float x = 0.0f;
        float y = 0.0f;
        float z = 0.0f;
        float signX = 0.0f;
        float signY = 0.0f;

        int lightCounts = m_LightsCountInput.text.Length <= 0 ? 0 : int.Parse(m_LightsCountInput.text);

        for(int i = 0; i < lightCounts; ++i)
        {
            z = Random.Range(MinZ, MaxZ);
            
            MaxY = z * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            MaxX = m_Camera.aspect * MaxY;
            
            x = Random.Range(MinX, MaxX);
            y = Random.Range(MinY, MaxY);
            
            signX = Random.Range(-1.0f, 1.0f);
            signY = Random.Range(-1.0f, 1.0f);

            GameObject go = new GameObject();
            go.transform.position = m_Camera.transform.localToWorldMatrix * new Vector4(x * signX, y * signY, z, 1.0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = Random.Range(1.0f, 1.0f);
            l.color = new Color(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f));
            l.intensity = Random.Range(2.0f, 3.0f);

            go.transform.parent = m_LightsGroupObject.transform;
        }
    }
}
