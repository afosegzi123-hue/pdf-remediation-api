import psycopg2
conn = psycopg2.connect('host=aws-1-eu-west-1.pooler.supabase.com port=6543 dbname=postgres user=postgres.hnyflybpvbpinrhdqimp password=Qas1#jdhkda')
cur = conn.cursor()
cur.execute('SELECT column_name, data_type FROM information_schema.columns WHERE table_name = \'RemediationLogs\'')
print(cur.fetchall())
conn.close()
