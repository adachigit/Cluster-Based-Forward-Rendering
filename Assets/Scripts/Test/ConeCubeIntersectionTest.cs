using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConeCubeIntersectionTest : MonoBehaviour
{
    struct Plane
    {
        public Plane(Vector3 normal, float distance)
        {
            N = normal;
            d = distance;
        }

        public Vector3 N;
        public float d;
    };

    struct Cone
    {
        public Vector3 T;
        public float h;
        public Vector3 d;
        public float r;
    };

    public BoxCollider cube;
    public Light spotLight;

    Plane nearPlane;
    Plane farPlane;
    Plane topPlane;
    Plane bottomPlane;
    Plane leftPlane;
    Plane rightPlane;
    
    // Start is called before the first frame update
    void Start()
    {
    }

    bool PointInsidePlane(Vector3 p, Plane plane)
    {
        float pDistancePlane = Vector3.Dot(plane.N, p);

        return pDistancePlane - plane.d >= 0;
    }

    bool ConeInsidePlane(Cone cone, Plane plane)
    {
        Vector3 m = Vector3.Cross(Vector3.Cross(plane.N, cone.d), cone.d);
        Vector3 Q = cone.T + cone.d * cone.h + m * cone.r;

        return PointInsidePlane(cone.T, plane) && PointInsidePlane(Q, plane);
    }

    Plane BuildPlane(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        Plane plane;

        Vector3 v0 = p1 - p0;
        Vector3 v2 = p2 - p0;

        plane.N = Vector3.Normalize(Vector3.Cross(v0, v2));

        plane.d = Vector3.Dot(plane.N, p0);

        return plane;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 halfSize = cube.transform.localScale / 2.0f;
        Vector3 pNearLeftBottom = new Vector3(cube.transform.position.x - halfSize.x,
                                              cube.transform.position.y - halfSize.y,
                                              cube.transform.position.z - halfSize.z);
        Vector3 pFarRightTop = new Vector3(cube.transform.position.x + halfSize.x,
                                           cube.transform.position.y + halfSize.y,
                                           cube.transform.position.z + halfSize.z);

        nearPlane = BuildPlane(new Vector3(pNearLeftBottom.x, pNearLeftBottom.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pFarRightTop.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pNearLeftBottom.y, pNearLeftBottom.z));
        farPlane = BuildPlane(new Vector3(pFarRightTop.x, pNearLeftBottom.y, pFarRightTop.z),
                               new Vector3(pNearLeftBottom.x, pFarRightTop.y, pFarRightTop.z),
                               new Vector3(pNearLeftBottom.x, pNearLeftBottom.y, pFarRightTop.z));
        topPlane = BuildPlane(new Vector3(pNearLeftBottom.x, pFarRightTop.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pFarRightTop.y, pFarRightTop.z),
                               new Vector3(pFarRightTop.x, pFarRightTop.y, pNearLeftBottom.z));
        bottomPlane = BuildPlane(new Vector3(pNearLeftBottom.x, pNearLeftBottom.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pNearLeftBottom.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pNearLeftBottom.y, pFarRightTop.z));
        leftPlane = BuildPlane(new Vector3(pNearLeftBottom.x, pNearLeftBottom.y, pNearLeftBottom.z),
                               new Vector3(pNearLeftBottom.x, pFarRightTop.y, pFarRightTop.z),
                               new Vector3(pNearLeftBottom.x, pFarRightTop.y, pNearLeftBottom.z));
        rightPlane = BuildPlane(new Vector3(pFarRightTop.x, pNearLeftBottom.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pFarRightTop.y, pNearLeftBottom.z),
                               new Vector3(pFarRightTop.x, pFarRightTop.y, pFarRightTop.z));

        Cone cone = new Cone();
        cone.T = spotLight.transform.position;
        cone.h = spotLight.range;
        cone.d = spotLight.transform.localToWorldMatrix.GetColumn(2);
        cone.r = spotLight.range * Mathf.Tan(spotLight.spotAngle * 0.5f * Mathf.Deg2Rad);

        if(ConeInsidePlane(cone, topPlane) || ConeInsidePlane(cone, bottomPlane) || ConeInsidePlane(cone, nearPlane) ||
           ConeInsidePlane(cone, farPlane) || ConeInsidePlane(cone, leftPlane) || ConeInsidePlane(cone, rightPlane))
        {
            Debug.Log("Out of AABB");
        }

/*
        if(ConeInsidePlane(cone, leftPlane))
        {
            Debug.Log("Out of leftPlane");
        }
*/
    }
}
