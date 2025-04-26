# Navigation Samples

The Navigation Samples showcase various usages of the NavMesh. They contain eight samples:

1. Multiple Agent Sizes: Demonstrates how a different radius on an agent type can change the way agents navigate through the same scene.

2. Drop Plank: Demonstrates dynamically changing walkable paths by allowing the player to add walkable planks by pressing space.

3. Free Orientation: Demonstrates a controllable agent that can walk on a tilted plane.

4. Sliding Window Infinite: Demonstrates a controllable agent that can walk through a dynamically created world that gets updated to simulate infinity as the agent walks through it. The NavMesh is only built in some set bounds that follow the agent.

5. Sliding Window Terrain: Demonstrates a controllable agent that can walk through a terrain for which the NavMesh is only generated within a set distance of the agent.

6. Modify Mesh: Demonstrates agents walking aimlessly on planes whose mesh can be modified dynamically by the player.

7. Dungeon: Demonstrates a controllable agent that can walk through a maze generated from pre-baked tiles that connect to each other at runtime. The link traversal animation can be modified with some presets (teleport, normal speed, parabola, curve).

8. Height Mesh: Demonstrates two agents walking down stairs. The environment on the left uses `NavMeshSurface` with a Height Mesh which allows the agent to snap to each step in the stairs as it goes down. The environment on the right uses a `NavMeshSurface` with no Height Mesh; the agent simply slides down the stairs.

Note that some of these samples require that the `Packages/manifest.json` file of your project [references](https://docs.unity3d.com/Manual/upm-manifestPrj.html) the following default modules:
```
"com.unity.modules.physics": "1.0.0",
"com.unity.modules.terrain": "1.0.0",
"com.unity.modules.terrainphysics": "1.0.0"
```

# Introduction to NavMesh

The Navigation package allows you to set up pathfinding AI in your Unity project. Two fundamental concepts of pathfinding are (1) agents and (2) world representation.

1. An agent is a game entity that travels autonomously between two points in a scene. In Unity, a GameObject can be turned into a navigation agent by adding a `NavMeshAgent` component to it.

2. World representation is what allows the pathfinding program of an agent to understand the traversable surfaces of a world. It is a simplification of a 3D world. In Unity, a traversable surface is represented as a mesh of polygons which we refer to as NavMesh.

To convert some or all of the geometry in your scene into a surface that is traversable by an agent, you can use the `NavMeshSurface` component. However, you must also generate the data of the `NavMeshSurface` by using the `Bake` button in the Inspector. The process of baking is what actually creates a representation of the geometry in your scene that agents and their pathfinding program can understand.

Whenever there are modifications done to the scene's geometry that can impact the navigation, the related `NavMeshSurface` component must be rebaked. The baking process is not done automatically because it can be a long process depending on the size and complexity of the input geometry. Note that baking cannot be done from the Inspector during Playmode.

In order for an agent to move, it must know its destination. In Unity, the destination of a `NavMeshAgent` can be set through code with the `destination` property or the `SetDestination()` method. You can find an example of this in the `ClickToMove` script.

For more information, refer to the [AI Navigation package manual](https://docs.unity3d.com/Packages/com.unity.ai.navigation@latest).

# Agent Types

The following agent types are created and used by the samples:

    1.  Name: Humanoid for Navigation Sample
        Radius: 0.5
        Height: 2.0
        Step Height: 0.75
        Max Slope: 45


    2.  Name: Ogre for Navigation Sample
        Radius: 1.0
        Height: 2.0
        Step Height: 0.4
        Max Slope: 36
