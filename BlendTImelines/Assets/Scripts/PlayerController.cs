using UnityEngine;
using UnityEngine.AI;

public class PlayerController : MonoBehaviour
{
    public Camera Cam;
    public NavMeshAgent Agent;
    public AnimatorController Character;
    public Rigidbody Body;
    private bool _isAgentEnabled;

    void Start()
    {
        if (Cam == null)
        {
            Cam = Camera.main;
        }

        if (Agent == null)
        {
            Agent = GetComponent<NavMeshAgent>();
        }

        if (Character == null)
        {
            Character = GetComponent<AnimatorController>();
        }

        if (Body == null)
        {
            Body = GetComponent<Rigidbody>();
        }
    }

    // TODO: Disable when timeline animation is in full swing.
    public void DisableAgentControl()
    {
        _isAgentEnabled = false;
        Body.angularVelocity = Vector3.zero;
        Body.linearVelocity = Vector3.zero;
        Agent.transform.position = Character.Animator.transform.position;
        Agent.transform.rotation = Character.Animator.transform.rotation;
        Agent.updatePosition = false;
        Agent.updateRotation = false;
    }

    public void EnableAgentControl()
    {
        Agent.updatePosition = true;
        Agent.updateRotation = true;
        Body.angularVelocity = Vector3.zero;
        Body.linearVelocity = Vector3.zero;
        _isAgentEnabled = true;
    }

    void Update()
    {
        if (!_isAgentEnabled)
            EnableAgentControl();

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                Agent.SetDestination(hit.point);
            }
        }

        if (_isAgentEnabled)
        {
            if (Agent.remainingDistance < Agent.stoppingDistance)
            {
                Agent.SetDestination(transform.position);
                var delta = transform.position - Agent.destination;
                Character.UpdateAnimation(delta);
            }
            else
            {
                Character.UpdateAnimation(Agent.desiredVelocity);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (Agent != null)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(Agent.transform.position, Agent.destination);
        }
    }
}