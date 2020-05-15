using UnityEngine;

public class MoveController : MonoBehaviour
{
    [SerializeField]
    public Camera camera;
    [SerializeField]
    public float speed;

    public void OnForwardClick()
    {
        camera.transform.localPosition = camera.transform.localPosition + camera.transform.forward * speed;
    }

    public void OnBackwardClick()
    {
        camera.transform.localPosition = camera.transform.localPosition - camera.transform.forward * speed;
    }
}
