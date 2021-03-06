﻿using Windows.Kinect;
using UnityEngine;
using UnityEngine.Networking;

//The Class userSynPosition is responsible for handling the movement of each tracked user as well as synchronizing the this data over the network
public class UserSyncPosition : NetworkBehaviour
{
    //The SyncVar attribute specifies that if this variable changes on the server it changes for this instance of the class on the clients 
    //syncPos stores the position of the user over the network
    [SyncVar] public Vector3 syncPos;
    //syncRot stores the rotation of the user over the network
    [SyncVar] public Vector3 syncRot;

    //The Offset booleans determine wether or not to offset the movement of this object by calibration data
    [SyncVar] public bool positionalOffset;
    [SyncVar] public bool rotationalOffset;

    //The color of the user, and the name of it is synchronized over the network and saved in theese variables
    [SyncVar] private Color userColor;
    [SyncVar] private string objectName;

    //The SerializeField attribute allows private variables to be visible in the inspector in unity
    //Reference to the transform class connected to this gameObject
    [SerializeField] private Transform myTransform;
    //The speed at which this class moves to it's synchronized position on other clients 
    [SerializeField] private float lerpRate = 15;

    //Mirror the movement aoround the x and z axis
    public bool MirroredMovement;
    //This value is only true during a calibration scene
    public bool isCalibrationUser = true;

    //Reference to the kinectmanager class
    private KinectManager manager = KinectManager.Instance;
    //Reference to the offsetcalculator
    private OffsetCalculator offsetCalculator;
    //Reference to the object which controlls multiple users
    private UserController PlayerObject;

    private bool isTrackingLost = true;

    //Synchronization delay, that is only synchronize every 100th second to reduce packet size
    private float syncStep = 0.1f;
    //Amount of timePassed
    private float timePassed;
    //The name of this object
    public string userId;

    [SyncVar]public bool isGivenJoint = false;
    [SyncVar]public int jointNum = -1;

    [SyncVar]public float beamAngle;
    // Update is called once per frame
    void Update()
    {
        KinectSensor kinect = KinectSensor.GetDefault();

        if (kinect.IsAvailable)
        {
            //CmdProvideBeamAngleToServer(kinect.AudioSource.AudioBeams[0].BeamAngle);
        }
        //If the L key is pressed on the keyboard call the method LogPosition
        if (Input.GetKeyUp(KeyCode.L))
        {
            LogPosition();
        }
    }


    [ClientCallback]
    //Fixed update runs every physics step instead of every frame. The physics step is usually in lockstep with a timed value therefore better for networking
    void FixedUpdate () {
        //If this is a calibration user control its own movement
        if (isCalibrationUser)
        {
            if (isGivenJoint && hasAuthority)
            {
                TransmitPosition(jointNum);
            }
            else TransmitPosition();
        }
        //Call lerpPosition
        LerpPosition();
    }

    //OnstartLocalPlayer will run once a soon an instance of this class connects to a server and only the client which created it
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        //Set the name of the gameobject to equal the networkidentity value of the client
        string objectName = "User " + (GetComponent<NetworkIdentity>().netId.Value - 1);
        //Set its colour to a random value
        userColor = new Color(Random.value, Random.value, Random.value);
        //Set this random colour to the object
        transform.GetComponent<MeshRenderer>().material.color = userColor;
        // Set the name to this object
        transform.name = objectName;
        //Call the method to change the clour and name of this gameobject on the network.
        Cmd_ChangeIdentity(userColor, objectName);
    }

    //Initialize the user method only used by the UserController class
    public void Initialize(string id, Color userColor)
    {
        //If this is not an object´created by the current client
        if (!isLocalPlayer)
        {
            //Assign all the SyncVar variables from the network to this gameObject
            this.objectName = "SubUser " + id;
            this.userColor = userColor;
            transform.GetComponent<MeshRenderer>().material.color = userColor;
            transform.name = objectName;
        }
    }

    //LerpPosition is responsible for moving this user if it exists on a client but is not controlled by the client i.e !isLocalPlayer
    void LerpPosition()
    {
        if (!isLocalPlayer)
        {
            //Linearly interpolate between the current position to the syncPos, and the rotation and the syncRot, at the speed of lerpRate
            myTransform.position = Vector3.Lerp(myTransform.position, syncPos, Time.deltaTime*lerpRate);
            myTransform.rotation = Quaternion.Lerp(myTransform.rotation, Quaternion.Euler(syncRot), Time.deltaTime * lerpRate);
            //Set the colour and the name to the syncronized values
            transform.GetComponent<MeshRenderer>().material.color = userColor;
            transform.name = objectName;
        }
    }

    [Command]
    //CmdProvidePositionToServer recieves two vector3 vars from a client which it will then update on the server,
    //which in turn will update it on all other clients
    public void CmdProvidePositionToServer(Vector3 pos, Vector3 rot)
    {
        syncPos = pos;
        syncRot = rot;
    }

    [Command]
    //CmdProvideBeamAngleToServer recieves one float from a client which it will then update on the server,
    //which in turn will update it on all other clients
    public void CmdProvideBeamAngleToServer(float _beamAngle)
    {
        beamAngle = _beamAngle;
    }

    [Command]
    //Cmd_ChangeIdentity changes the colour of this object on the server which in turn will update it on all other clients
    public void Cmd_ChangeIdentity(Color col, string objectName)
    {
        this.objectName = objectName;
        userColor = col;
        //Spawn this object on the network for good measure, to ensure again that commands are able to be called from other classes
        NetworkServer.Spawn(gameObject);
    }

    //The transmitPosition method is responsible for moving this gameObject
    public void TransmitPosition()
    {
        //Only move if being controlled by the client which created this gameObject
        if (isLocalPlayer && isCalibrationUser)
        {
            //Check if the KinectManager class is accesisble
            if (manager == null)
            {
                manager = KinectManager.Instance;
                
            }
            else
            {
                //Debug.Log("Transmitting pos or wha5tever");
                //The KinectManager must be there therefore Move & Orient to tracked individual and Tell the server my new posistion and rotation
                MoveWithUser();
                OrientWithUser();
                CmdProvidePositionToServer(myTransform.position, myTransform.rotation.eulerAngles);
            }
        }
    }

    public void TransmitPosition(int jointNum)
    {
        //Only move if being controlled by the client which created this gameObject
        if (hasAuthority && isCalibrationUser)
        {
            //Check if the KinectManager class is accesisble
            if (manager == null)
            {
                manager = KinectManager.Instance;

            }
            else
            {
                //The KinecManager must be there therefore Move & Orient to tracked individual and Tell the server my new posistion and rotation
                //
                int users = manager.GetUsersCount();
                if (users == 0)
                    return;
                long user1Id = manager.GetUserIdByIndex(0);

                int user1BodyIndex = manager.GetBodyIndexByUserId(user1Id);
                //KinectInterop.BodyData user1Body = manager.bodyFrame.bodyData[user1BodyIndex].joint[jointNum].position;
                //
                //uint playerID = manager != null ? manager.GetPlayer1ID() : 0;
                MoveWithUser(manager.bodyFrame.bodyData[user1BodyIndex].joint[jointNum].position);
                OrientWithUser();
                CmdProvidePositionToServer(myTransform.position, myTransform.rotation.eulerAngles);
            }
        }
    }

    //Client attribute means that this method will only run on clients
    [Client]
    public void MoveWithUser()
    {
        //Get the instance of the offsetCalculator class
        offsetCalculator = OffsetCalculator.offsetCalculator;
        //Get the position of the first tracked user
        Vector3 posPointMan = manager.GetJointPosition(manager.GetUserIdByIndex(0), 0);
        for (int i = 0; i < manager.GetUsersCount(); i++)
        {
            //Debug.Log("User "+ i + manager.GetJointPosition(manager.GetUserIdByIndex(i),0));
        }
        //Flip movement on the z axis of Mirrored movement is true
        posPointMan.z = !MirroredMovement ? -posPointMan.z : posPointMan.z;
        posPointMan.x *= 1;

        //If rotationalOffset is true get the rotationalOffset from the offsetCalculator
        if (rotationalOffset)
        {
            //Get A new movement direction based on the rotationalOffset vector around the y and the x axis, that is Tilt and rotation
            Quaternion directionY = Quaternion.AngleAxis(offsetCalculator.rotationalOffset.y, Vector3.up);
            Quaternion directionX = Quaternion.AngleAxis(offsetCalculator.rotationalOffset.x, Vector3.right);
            Quaternion direction = directionX*directionY;

            //Apply this movement direction to the tracked position of the user
            posPointMan = (direction * posPointMan) != Vector3.zero ? (direction * posPointMan) : posPointMan;
            //Then apply this transformed position to this gameObject
            transform.position = posPointMan;
        }

        if (positionalOffset)
        {
            //If positional offset is true mereley apply the offset vector3 from the offsetCalculator to the current position
            posPointMan += offsetCalculator.positionalOffset;
            transform.position = posPointMan;
        }
        else
        {
            //Simply apply the tracking information directly from the kinect to this GameObject
            transform.position = posPointMan;
        }
    }

    //Overloaded method which is only used by the UserController recieves a position to move to 
    //rather than getting one itself from the kinectmanager
    [Client]
    public void MoveWithUser(Vector3 posPointMan)
    {
        offsetCalculator = OffsetCalculator.offsetCalculator;
        posPointMan.z = !MirroredMovement ? -posPointMan.z : posPointMan.z;
        posPointMan.x *= 1;

        if (rotationalOffset)
        {
            //Increase the timepassed by a timebased increment
            timePassed += Time.deltaTime;
            //Every 100th second send the position of this gameObject over the server to avoid packet overload
            if (timePassed >= syncStep)
            {
                CmdProvidePositionToServer(myTransform.position, myTransform.rotation.eulerAngles);
                timePassed = 0;
            }
            else
            {
                //Apply both the rotation and the position offset to this gameObject as seen in original method
                Quaternion directionY = Quaternion.AngleAxis(offsetCalculator.rotationalOffset.y, Vector3.up);
                Quaternion directionX = Quaternion.AngleAxis(offsetCalculator.rotationalOffset.x, Vector3.left);
                Quaternion direction = directionX * directionY;

                posPointMan = (direction * posPointMan) != Vector3.zero ? (direction * posPointMan) : posPointMan;
                transform.position = posPointMan;
                posPointMan += offsetCalculator.positionalOffset;
                transform.position = posPointMan;
            }
        }
        else
        {
            //If not offset merely move the cube and send position to server every 100th second
            timePassed += Time.deltaTime;
            if (timePassed >= syncStep)
            {
                CmdProvidePositionToServer(myTransform.position, myTransform.rotation.eulerAngles);
                timePassed = 0;
            }
            else
            {
                //Simply move to recieved position
                transform.position = posPointMan;
            }
        }
    }

    void LogPosition()
    {
        //Call Logdata static method to save the current position of the gameObject aswell as the id of the client who created this object
        Logger.LogData("Logging Position", transform.position, transform.rotation.eulerAngles,
            userId, "No time Logged " + (GetComponent<NetworkIdentity>().netId.Value - 1));
    }

    [Client]
    //This method is responsible for orienting the cube so it rotation and tilt coressponds to the tracked person orientation
    private void OrientWithUser()
    {
        //If a skeleteon is tracked
        if (manager.IsUserDetected())
        {
            Quaternion userOrientation = manager.GetJointOrientation(manager.GetUserIdByIndex(0), 0, false);
            //Quaternion userOrientation = manager.GetUserOrientation(0, false);

                if (rotationalOffset)
                {
                    //Offset the rotation by the offsetcalculators offsetVector and apply to this gameObject
                    userOrientation.eulerAngles -= new Vector3(offsetCalculator.rotationalOffset.x, offsetCalculator.rotationalOffset.y, 0);
                    myTransform.rotation = userOrientation;
                }
                else
                {
                    //Simple Apply this rotation to this gameObject
                    myTransform.rotation = userOrientation;
                }
        }
    }
}
