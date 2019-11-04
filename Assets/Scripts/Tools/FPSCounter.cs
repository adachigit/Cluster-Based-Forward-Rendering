using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FPSCounter : MonoBehaviour
{
    public Text m_textFPS;

    private float m_lastCountTime;
    private int m_fpsCounter;

    // Start is called before the first frame update
    void Start()
    {
        m_textFPS.text = "0";
        m_lastCountTime = 0.0f;
        m_fpsCounter = 0;
    }

    // Update is called once per frame
    void Update()
    {
        if((Time.time - m_lastCountTime) >= 1)
        {
            m_textFPS.text = "" + m_fpsCounter;
            m_lastCountTime = Time.time;
            m_fpsCounter = 0;
        }

        ++m_fpsCounter;
    }
}
