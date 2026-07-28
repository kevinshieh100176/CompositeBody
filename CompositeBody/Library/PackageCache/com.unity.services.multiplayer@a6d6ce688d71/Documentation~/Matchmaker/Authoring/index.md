# Matchmaker Authoring
This module allows users to author, modify, and deploy Matchmaker assets directly from the Unity Editor,
into any environment.

These operations are also supported from [the UGS CLI][1]

> NOTE: Matchmaker Authoring is only supported on Unity 6.0 and above.

### Deployment Window
The Deployment Window is a core feature of the Deployment package.

The Deployment Window is a core feature of the Deployment package. Its purpose is to provide all services with a single, cohesive interface for deployment needs, allowing you to upload cloud assets for your respective cloud service.

For more information, consult the [com.unity.services.deployment](https://docs.unity3d.com/Packages/com.unity.services.deployment@latest) package documentation.

### Create Matchmaker Assets
Right-click in the Project Window then select `Create > Services > Matchmaker Queue Config` to create a Matchmaker configuration file.

The Deployment Window automatically detects these files to be deployed at a later time.

For more information on how to create and modify Matchmaker Assets, please see the [Matchmaker assets](matchmaker-assets.md) documentation.

[1]:https://services.docs.unity.com/guides/ugs-cli/latest/general/base-commands/deploy/
