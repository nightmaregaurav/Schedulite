using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

public sealed class SkellyJob(TimeProvider timeProvider, ILogger<SkellyJob> logger) : IBackgroundJob
{
    public ExecutionConcurrencyScope ConcurrencyScope => ExecutionConcurrencyScope.PerSubject;
    public string JobName => "Skelly Printer";
    public string JobDescription => "Draws a skeleton, waits for 10 seconds to simulate work, then says bye.";

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
$"""
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
*              ___           _,.---,---.,_                                    *
*              |         ,;~'             '~;,                                *
*              |       ,;                     ;,                              *
*     Frontal  |      ;                         ; ,--- Supraorbital Foramen   *
*      Bone    |     ,'                         /'                            *
*              |    ,;                        /' ;,                           *
*              |    ; ;      .           . <-'  ; |                           *
*              |__  | ;   ______       ______   ;<----- Coronal Suture        *
*             ___   |  '/~"     ~" . "~     "~\'  |                           *
*             |     |  ~  ,-~~~^~, | ,~^~~~-,  ~  |                           *
*   Maxilla,  |      |   |        {"}"}:{"{"}        | <------ Orbit              *
*  Nasal and  |      |   l       / | \       !   |                            *
*  Zygomatic  |      .~  (__,.--" .^. "--.,__)  ~.                            *
*    Bones    |      |    ----;' / | \ `;-<--------- Infraorbital Foramen     *
*             |__     \__.       \/^\/       .__/                             *
*                ___   V| \                 / |V <--- Mastoid Process         *
*                |      | |T~\___!___!___/~T| |                               *
*                |      | |`IIII_I_I_I_IIII'| |                               *
*       Mandible |      |  \,III I I I III,/  |                               *
*                |       \   `~~~~~~~~~~'    /                                *
*                |         \   .       . <-x---- Mental Foramen               *
*                |__         \.    ^    ./                                    *
*                              ^~~~^~~~^                                      *
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
{timeProvider.GetUtcNow():O}] Hi from the {nameof(SkellyJob)} background job! context: {JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}
"""
        );
        logger.LogInformation("Printed the skeleton for subject {SubjectId} in execution {ExecutionId}.", context.SubjectId, context.ExecutionId);
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        logger.LogInformation("Finished the skeleton job for subject {SubjectId} in execution {ExecutionId}.", context.SubjectId, context.ExecutionId);
    }
}
