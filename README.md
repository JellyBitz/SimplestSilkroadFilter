# SimplestSilkroadFilter

Code base to make an asynchornized Silkroad Online Server proxy aka filter for vSRO 188.
You'll be able to set a proxy layer between you and your game server, giving you the possibility to read/write/send/limit packets without too much effort.

The default proxy application setup has been made to support connections outside your local machine through a public host. 

## Usage
- `-gw-host=` : `*` Host or IP from the SRO server to connect
- `-gw-port=` : `*` Port from the Silkroad server to connect
- `-bind-gw-port=` : Port this proxy gonna use to behave as Gateway server (Recommended)
- `-bind-gw-port=` : Port this proxy gonna use to behave as Agent server
- `-bind-gw-port=` : Port this proxy gonna use to behave as Download server
- `-public-host=` : Public Host or IP you will use for external connections to your private network

## Example

```bat
SimplestSilkroadFilter.exe -gw-host=192.168.1.121 -gw-port=15779 -bind-gw-port=15778 -public-host=mysro.ddns.net
```

> *Please, don't take the application too seriously. It has been made for academy purposes mostly.*
