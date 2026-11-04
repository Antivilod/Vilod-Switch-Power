using System.Security.AccessControl;
using System.Security.Principal;

namespace QuietPC.Service;

// The elevated backend must never consume recovery files writable by other users.
static class StateDirectory {
 public static string PathName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"VilodVSP");
 public static void Prepare(string path) {
  var directory=new DirectoryInfo(path);
  if(directory.Exists){
   Check(directory);
   foreach(var entry in directory.EnumerateFileSystemInfos()){
    if(entry is DirectoryInfo)throw new IOException("Unexpected recovery subdirectory");
    Check(entry);
   }
   return;
  }
  var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
  acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
  foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})
   acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,
    InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  FileSystemAclExtensions.CreateDirectory(acl,path);
  Check(new DirectoryInfo(path));
 }
 static void Check(FileSystemInfo entry){
  if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected recovery path is not allowed");
  FileSystemSecurity acl=entry is DirectoryInfo directory?directory.GetAccessControl():((FileInfo)entry).GetAccessControl();
  var owner=acl.GetOwner(typeof(SecurityIdentifier))?.Value;
  if(owner is not "S-1-5-18" and not "S-1-5-32-544")throw new IOException("Untrusted recovery owner");
  const FileSystemRights write=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
  foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){
   if(rule.AccessControlType==AccessControlType.Allow&&(rule.FileSystemRights&write)!=0&&
    rule.IdentityReference.Value is not "S-1-5-18" and not "S-1-5-32-544")throw new IOException("Recovery files must be administrator-owned and protected");
  }
 }
}
