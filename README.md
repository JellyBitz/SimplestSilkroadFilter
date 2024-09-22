# SimplestSilkroadFilter

Code base to make an asynchornized Silkroad Online Server proxy aka filter for vSRO 188.
You'll be able to set a proxy layer between you and your game server, giving you the possibility to read/write/send/limit packets without too much effort.

The default proxy application setup has been made to support connections outside your local machine through a public host.
The current state does not support client updates since it will require to set up another server and I wanted to make this simpler for demostration purposes.

## Usage
- `-bind-port` : Port this proxy gonna use to behave as Gateway
- `-gw-host` : Host or IP from the Silkroad Online server to connect
- `-gw-port` : Port from the Silkroad Online server to connect
- `-public-host` : (Optional) Host you'll use to redirect connections outside your local machine

## Example

```bat
SimplestSilkroadFilter.exe -bind-port=15777 -gw-host=192.168.1.121 -gw-port=15779 -public-host=mysro.ddns.net
```

> *Please, take the application as an example since it has been made for academy purposes only.*