# SimplestSilkroadFilter

Code base to make an asynchornized Silkroad Server Filter.

You'll be able to set a proxy layer between you and your silkroad server, giving you the possibility to read/write/send/limit packets without too much effort.  
Please, take the code as example since it has been made for academy purposes only.

---

The application example comes with the way to bypass restricted packets from phbot plugins.  
At this case you'll be able to read SPAWN/DESPAWN packets from plugins by using a different opcode `0xF00D` which contains the following structure:

```
ushort PacketOpcode
* byte PacketBytes
```
