* How can I find out when a major event has
occurred somewhere in the world? Is there already a feature or service that I
can query to retrieve this information? Can I filter events by categories such
as breaking news, market movements, natural disasters, and similar types of
events?

* Can these events be retrieved and saved into
individual files that can be stored somewhere accessible over the internet? Alternatively,
is it possible to access this information programmatically by querying ChatGPT
from an application?

**News Test Prompt**

* Search for the latest news from around the world
over the past 24 hours. Categories: Breaking news, Market movements, Natural
disasters, Geopolitics. Importance: Include only major events of international
significance. Categorize the events and provide exact timestamps, locations,
brief summaries, significance assessments, and source links. Do not include
duplicate events. Save the results in JSON format in a file named
world-news-{date-time}.json.

**Calling Codex from the Command Line**

* Is it possible to invoke Codex from the command
line and pass it a prompt?

* how can we get "immediate"
notification when a "big" event happens?

* How do you send a message to Slack from a
program?

* Can you send direct messages to slack users?

[hiányző promptok]

What database would you recommend for an app like this?
Would SQLite be sufficient, or would I need PostgreSQL running in the
background? For now, I only need it for a take-home assignment as part of a job
application.

**Plan prompt**

```
Create a plan for a .NET application that will serve as the
backend for an Angular application, and an Angular project that will serve as
the frontend.

Users should be able to register with their email addresses
and subscribe to different types of news.

Categories:

- Breaking
   news
- Market
   movements
- Natural
   disasters
- Geopolitics
- Technology
   & AI
- Economy
- Health

Time periods:

- Last
   6 hours
- Last
   24 hours
- Last
   3 days
- Last
   7 days

Importance:

- Only
   events of exceptional importance
- All
   significant events

The system should also send news to Slack. Users should be
able to subscribe by connecting a Slack workspace and selecting their preferred
news categories.

Implement news delivery through an interface-based
abstraction to support future extensions to other communication channels.

There should be an admin panel where administrators can view
and manage users, see which news retrieval providers are being used, and which
delivery channels are supported.

News retrieval should also be implemented through an
interface-based abstraction to allow future integration with Grok or other AI
providers.

A background process should retrieve news every hour, with a
configurable execution interval, and store the retrieved data in the database.

Users should be able to select the channels through which
they want to receive notifications. Initially, email and Slack should be
available.

Email notifications should use a predefined email template.

For Slack notifications, I want the option to use my own
public Slack workspace.

The system must check whether a news item has already been
sent to a user to prevent duplicate deliveries. For Slack, it should also track
whether a news item has already been posted to the relevant destination.

News delivery can be handled by a background job. However,
when a new user subscribes, the system should also be able to send an initial
email immediately after subscription.

Use SQLite as the database, with Entity Framework Core as
the ORM.

The Codex-based news retrieval provider should be
implemented as a task that returns JSON using the command codex exec
"query".

Define the expected JSON response structure explicitly
within the query sent to Codex.

If you have any questions, you ask me.  
```

* implement the plan what is in the development-plan
and the implementation plan markdown files. Add resend.com option as one of
email provider

* give me some good initial commit message

* The codex fetch not working. Create a test that
calls the codexnewsprovider with Breaking news and Geopolitics within the 3
hours. write the result to console. i'd like to see the FetchAsync is called

* The test failed. why don't you just add the
prompt as argument not the input this works in powershell:`
codex exec "Search for fresh
news from around the world in the past 24 hours. Categories: ...." -o
$OutputPath --skip-git-repo-check`
