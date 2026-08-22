import psycopg2
conn = psycopg2.connect('host=aws-1-eu-west-1.pooler.supabase.com port=6543 dbname=postgres user=postgres.hnyflybpvbpinrhdqimp password=Qas1#jdhkda')
cur = conn.cursor()
cur.execute('ALTER TABLE "RemediationLogs" ADD COLUMN "DownloadUrl" text NULL;')
cur.execute('ALTER TABLE "RemediationLogs" ADD COLUMN "RemediatedFileName" text NULL;')
conn.commit()
print('Tables altered successfully!')
