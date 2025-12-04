using UnityEngine;

public class CollisionHandler: MonoBehaviour
{
  

    // detecting objects with their tags 

    private void OnCollisionEnter(Collision obj)
    {
        switch (obj.gameObject.tag)
        {
            case "Start":
                Debug.Log(" Starting point");
                break;
            case "Finish":
                Debug.Log(" Complete the level");
                break;
            default:
                Debug.Log(" Rocket crashed");
                break;
        }
    }
   
}
