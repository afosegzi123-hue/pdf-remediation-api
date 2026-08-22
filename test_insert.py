import psycopg2
import uuid

conn = psycopg2.connect('host=aws-1-eu-west-1.pooler.supabase.com port=6543 dbname=postgres user=postgres.hnyflybpvbpinrhdqimp password=Qas1#jdhkda')
cur = conn.cursor()

# Get a valid BatchSessionId
cur.execute('SELECT "Id" FROM "BatchSessions" LIMIT 1')
batch_id = cur.fetchone()[0]

try:
    cur.execute('''
    INSERT INTO "RemediationLogs" 
    ("Id", "BatchSessionId", "OriginalFileName", "FileSizeBytes", "IsOcrApplied", "IsStructureRebuilt", "IsAccessibleTagged", "ProcessingDurationMs", "ErrorMessage", "DownloadUrl", "RemediatedFileName")
    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
    ''', (str(uuid.uuid4()), batch_id, 'test.pdf', 100, False, True, True, 500, None, 'http://test', 'remediated_test.pdf'))
    conn.commit()
    print("Insert succeeded!")
except Exception as e:
    print("Insert failed:", e)

conn.close()
