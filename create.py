import psycopg2
conn = psycopg2.connect('host=aws-1-eu-west-1.pooler.supabase.com port=6543 dbname=postgres user=postgres.hnyflybpvbpinrhdqimp password=Qas1#jdhkda')
cur = conn.cursor()
cur.execute('''
CREATE TABLE "BatchSessions" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "CreatedAt" timestamp with time zone NOT NULL,
    "TotalFiles" integer NOT NULL,
    "SuccessfulFiles" integer NOT NULL,
    "FailedFiles" integer NOT NULL,
    "Status" text NOT NULL
);
''')
cur.execute('''
CREATE TABLE "RemediationLogs" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "BatchSessionId" uuid NOT NULL,
    "OriginalFileName" text NOT NULL,
    "FileSizeBytes" bigint NOT NULL,
    "IsOcrApplied" boolean NOT NULL,
    "IsStructureRebuilt" boolean NOT NULL,
    "IsAccessibleTagged" boolean NOT NULL,
    "ProcessingDurationMs" integer NOT NULL,
    "ErrorMessage" text NULL,
    CONSTRAINT "FK_RemediationLogs_BatchSessions_BatchSessionId" FOREIGN KEY ("BatchSessionId") REFERENCES "BatchSessions" ("Id") ON DELETE CASCADE
);
''')
conn.commit()
print('Tables created successfully!')
