# Matchmaker assets

## Creation
Right-click in the `Project Window` then select `Create > Services > Matchmaker Queue Config`
or `Create > Services > Matchmaker Environment Config`.

### Matchmaker Environment Config

The Matchmaker environment configuration is a simple file that will help setup the environment-level settings:
The [default queue][1] and whether Matchmaker is enabled.
```JSON
{
  "enabled": true,
  "defaultQueueName": "default-queue"
}
```
### Matchmaker Queue Config

It is strongly recommended to configure [file associations in your IDE][2] to work with Matchmaker files.
This will make authoring the files significantly easier, by providing
auto-complete functionality in your preferred IDE.

The schema can be found [here][3].

A matchmaker queue file defines the a queue and [its pools][4].

More information can be found in the [Matchmaker general documentation][5]
and [API documentation][6]


This file controls

[1]:https://docs.unity.com/ugs/en-us/manual/matchmaker/manual/advanced-topics-queues-pools#Default_queue
[2]:https://services.docs.unity.com/guides/ugs-cli/latest/general/samples/ide-file-association/
[3]:https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-queue.schema.json
[4]:https://docs.unity.com/ugs/en-us/manual/matchmaker/manual/advanced-topics-queues-pools#Pools
[5]:https://docs.unity.com/ugs/en-us/manual/matchmaker/manual/matchmaker-overview
[6]:https://services.docs.unity.com/matchmaker-admin/v3/#tag/Matchmaker-Admin/operation/upsertQueueConfig

