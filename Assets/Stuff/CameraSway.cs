using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraSway : MonoBehaviour
{

    public GameObject focalPoint;
    public float swayDistance = 0.6f;
    public float swayPeriod = 4f;
    private Vector3 startingPosition;
    private Vector3 startingRight;
    private Vector3 startingUp;
    private Vector3 startingEulers;
    float localTime = 0f; 

    void Start()
    {
        startingPosition = transform.localPosition;
        startingRight = transform.right;
        startingUp = transform.up;
        startingEulers = transform.eulerAngles;
    }

    // Update is called once per frame
    void Update( )
    {
        localTime += Time.deltaTime;
        float s = Mathf.PingPong(localTime / swayPeriod, 1f);
        float smoothS = Mathf.SmoothStep(0f, 1f, s);
        float x = (smoothS * 2f - 1f) * swayDistance;
        transform.localPosition = startingPosition + x * startingRight;
        transform.LookAt(focalPoint.transform.position, startingUp);
    }
}
