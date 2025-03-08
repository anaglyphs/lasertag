using UnityEngine;

namespace AprilTag {

//
// Tag pose structure for storing an estimated pose
//
public struct TagPose
{
    public int ID { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }

    public TagPose(int id, Vector3 position, Quaternion rotation)
    {
        ID = id;
        Position = position;
        Rotation = rotation;
    }
}

} // namespace AprilTag
