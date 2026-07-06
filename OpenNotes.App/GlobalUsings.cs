// Resolve ambiguity: our TaskStatus vs System.Threading.Tasks.TaskStatus
global using TaskStatus = OpenNotes.Models.TaskStatus;
global using TaskPriority = OpenNotes.Models.TaskPriority;

// Ensure System.IO types are available in all files (needed for WPF temp project)
global using System.IO;
global using System.IO.Compression;
