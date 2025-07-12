// https://learn.microsoft.com/en-us/training/modules/develop-multi-agent-azure-ai-foundry/
// https://github.com/MicrosoftLearning/mslearn-ai-agents/blob/main/Labfiles/06-build-multi-agent-solution/Python/agent_triage.py

using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;

IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var projectEndpoint = configuration["ProjectEndpoint"];
var modelDeploymentName = configuration["ModelDeploymentName"];

var priorityAgentName = "priority_agent";
var priorityAgentInstructions = """
Assess how urgent a ticket is based on its description.

Respond with one of the following levels:
- High: User-facing or blocking issues
- Medium: Time-sensitive but not breaking anything
- Low: Cosmetic or non-urgent tasks

Only output the urgency level and a very brief explanation.
""";

var teamAgentName = "team_agent";
var teamAgentInstructions = """
Decide which team should own each ticket.

Choose from the following teams:
- Frontend
- Backend
- Infrastructure
- Marketing

Base your answer on the content of the ticket. Respond with the team name and a very brief explanation.
""";

var effortAgentName = "effort_agent";
var effortAgentInstructions = """
Estimate how much work each ticket will require.

Use the following scale:
- Small: Can be completed in a day
- Medium: 2-3 days of work
- Large: Multi-day or cross-team effort

Base your estimate on the complexity implied by the ticket. Respond with the effort level and a brief justification.
""";

var triageAgentInstructions = """
Triage the given ticket. Use the connected tools to determine the ticket's priority, 
which team it should be assigned to, and how much effort it may take.
""";

PersistentAgentsClient client = new(projectEndpoint, new DefaultAzureCredential());

PersistentAgent priorityAgent = client.Administration.CreateAgent(
    model: modelDeploymentName,
    name: priorityAgentName,
    instructions: priorityAgentInstructions,
    tools: []
);

var priorityAgentTool = new ConnectedAgentToolDefinition(new ConnectedAgentDetails(
    id: priorityAgent.Id,
    name: priorityAgentName,
    description: "Assess the priority of a ticket"
));

PersistentAgent teamAgent = client.Administration.CreateAgent(
    model: modelDeploymentName,
    name: teamAgentName,
    instructions: teamAgentInstructions,
    tools: []
);

var teamAgentTool = new ConnectedAgentToolDefinition(new ConnectedAgentDetails(
    id: teamAgent.Id,
    name: teamAgentName,
    description: "Determine which team should own a ticket"
));

PersistentAgent effortAgent = client.Administration.CreateAgent(
    model: modelDeploymentName,
    name: effortAgentName,
    instructions: effortAgentInstructions,
    tools: []
);

var effortAgentTool = new ConnectedAgentToolDefinition(new ConnectedAgentDetails(
    id: effortAgent.Id,
    name: effortAgentName,
    description: "Determines the effort required to complete the ticket"
));

PersistentAgent agent = client.Administration.CreateAgent(
    model: modelDeploymentName,
    name: "triage_agent",
    instructions: triageAgentInstructions,
    tools: [priorityAgentTool, teamAgentTool, effortAgentTool]
);

Console.WriteLine("Creating agent thread");
PersistentAgentThread thread = client.Threads.CreateThread();

var prompt = "Users can't reset their password from the mobile app.";

var message = client.Messages.CreateMessage(
    threadId: thread.Id,
    role: MessageRole.User,
    content: prompt
);

Console.WriteLine("Processing agent thread. Please wait...");
ThreadRun run = client.Runs.CreateRun(
    thread.Id,
    agent.Id);

//Poll for completion.
do
{
    Thread.Sleep(TimeSpan.FromMilliseconds(500));
    run = client.Runs.GetRun(thread.Id, run.Id);
}
while (run.Status == RunStatus.Queued
    || run.Status == RunStatus.InProgress
    || run.Status == RunStatus.RequiresAction);

//Get the messages in the PersistentAgentThread. Includes Agent (Assistant Role) and User (User Role) messages.
Pageable<PersistentThreadMessage> messages = client.Messages.GetMessages(
    threadId: thread.Id,
    order: ListSortOrder.Ascending);

//Display each message and open the image generated using CodeInterpreterToolDefinition.
foreach (PersistentThreadMessage threadMessage in messages)
{
    foreach (MessageContent content in threadMessage.ContentItems)
    {
        switch (content)
        {
            case MessageTextContent textItem:
                Console.WriteLine($"[{threadMessage.Role}]: {textItem.Text}");
                break;
        }
    }
}

//Clean up test resources.
client.Threads.DeleteThread(threadId: thread.Id);

Console.WriteLine("Cleaning up agents:");
client.Administration.DeleteAgent(agent.Id);
Console.WriteLine("Deleted triage agent.");

// Delete the connected agents when done
client.Administration.DeleteAgent(priorityAgent.Id);
Console.WriteLine("Deleted priority agent.");
client.Administration.DeleteAgent(teamAgent.Id);
Console.WriteLine("Deleted team agent.");
client.Administration.DeleteAgent(effortAgent.Id);
Console.WriteLine("Deleted effort agent.");