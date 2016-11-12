using System;
using UnityEngine;
using System.Collections;

public class ThrowExceptionTest : MonoBehaviour
{
    public bool Thrown;
	
	// Update is called once per frame
	void Update ()
	{
	    if (Thrown)
	    {
	        return;
	    }

	    Thrown = true;

	    CallAnotherMethod();
	}

    void CallAnotherMethod()
    {
        throw new Exception("this is a test");
    }
}
