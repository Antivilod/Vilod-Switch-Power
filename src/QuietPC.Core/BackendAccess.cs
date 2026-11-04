namespace QuietPC.Core;

public static class BackendAccess {
 public static bool Allows(string owner,string? caller,bool administrator,string command)=>
  caller==owner||administrator&&(command is "off" or "heartbeat" or "prepareuninstall");
}
