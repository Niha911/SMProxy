# SMProxy

SMProxy is a proxy for Minecraft, allowing users to inspect the Minecraft protocol. It currently supports protocol
version 60, which is used by Minecraft 1.5.

**Note**: SMProxy uses [Craft.Net](https://github.com/SirCmpwn/Craft.Net) for networking, by including it via a
submodule. Craft.Net often has support for newer versions of Minecraft in the `snapshot` branch. If you want to
test Minecraft snapshots, use the `snapshot` branch of Craft.Net when you build SMProxy.

## Using SMProxy

On Windows, you need Microsoft.NET (if you are on Windows Vista or higher, you have this).

On Linux and Mac, you need Mono 2.10 or better. Also, preface commands with `mono`, as if you were invoking a java
program.

The typical usage of SMProxy is to start a session and connect to the proxy. The simplest usage is this:

    SMProxy.exe

(On Linux/Mac, `mono SMProxy.exe`) This will make the proxy listen for connections on `127.0.0.1:25564`, and proxy
them to `127.0.0.1:25565`. It will create log files with the following file name format:

    log_dd-mm-yyyy_hh-mm-ss.txt

...in the working directory, where the letters represent the date and time the log was created.

### Command Line Parameters

The more advanced use is like so:

    SMProxy.exe [--options...] [remote address] [local endpoint]

All parameters are optional. The default remote address is `127.0.0.1:25565`, and the default local address is
`127.0.0.1:25564`. The remote address may be a domain, which is resolved before establishing a connection. In
the local endpoint, you may omit the IP address and only specify a port, in which case SMProxy will use `127.0.0.1`.

For example, to open a proxy to example.com:12345, and to listen on port 54321 locally, use this syntax:

    SMProxy.exe example.com:12345 54321

The available options are described below:

**--local-endpoint [endpoint]**: You may explicitly state the endpoint with this, in the same manner that you'd
  use to include it as the last parameter.

**--filter [packets...]**: [packets...] is a comma-delimited list of packet IDs, in hexadecimal. Only these packets
  will be logged.

**--omit-client**: Omits client->server packets from the log.

**--omit-server**: Omits server->client packets from the log.

**--password [password]**: Specifies a password for login with Minecraft.net. The lastlogin file will be used if this
  is not provided along with --username. If no lastlogin file is present, the proxy will fail to connect to servers in
  online mode.

**--unfilter [packets...]**: Similar to `--filter`, this will do the opposite, excluding the specified packets from
  the log.

**--username [name]**: Specifies a username for login with Minecraft.net. The lastlogin file will be used if this is
  not provided along with --password. If no lastlogin file is present, the proxy will fail to connect to servers in
  online mode.

## Reading Logs

SMProxy creates packet logs for debugging purposes. Here is an example packet:

    {12:43:54.683} [SERVER->CLIENT] PlayerPositionAndLook (0x0D)
     [
      0D 40 47 55 03 91 70 12 3D 40 52 E7 AE 14 80 00  . . G U . . p . . . R . . . . . 
      00 40 52 80 00 00 00 00 00 40 70 26 C3 90 BA D0  . . R . . . . . . . p . . . . . 
      EA C2 A2 00 0F 42 10 00 06 00                    . . . . . B . . . . 
     ]
     X (Double): 46.6641713902686
     Y (Double): 75.6200000047684
     Stance (Double): 74
     Z (Double): 258.422745446921
     Yaw (Single): -81.00011
     Pitch (Single): 36.00002
     On Ground (Boolean): False

Included is the time this packet was logged (`{HH:MM:SS.milliseconds}`), the direction it was sent (`[SERVER->CLIENT]`),
the friendly name (`PlayerPositionAndLook`), and the packet ID (`0x0D`). Below is a hexadecimal dump of the raw TCP packet
contents, as well as those bytes interpreted as ASCII on the right. If the byte is not an ASCII letter or number, '.' is
inserted in its place. Finally, each field SMProxy recognizes is included in the log.

## Encryption and Online Mode

All vanilla connections use encryption, and most 3rd party clients and servers do as well. In order to enable sniffing of
an encrypted connection, SMProxy will set up two seperate encrypted connections. The consequences of this are:

* In a packet log, the encryption-related packet (0xFD and 0xFC) are likely to be logged incorrectly. SMProxy creates its
  own packets to send to the client and server seperately, and intercepts the packets that would usually be sent to each.
  SMProxy also creates its own RSA keypair, and an AES shared secret, and the packet log does not reflect these values.
* For online mode servers, SMProxy must authenticate with Minecraft.net on its own. You will likely not notice this, as
  SMProxy takes your username and password from your lastlogin file in .minecraft. However, if there is no lastlogin file,
  you must explicitly provide your username and password to Minecraft.net with `--username` and `--password` at the
  command line to connect to online mode servers.